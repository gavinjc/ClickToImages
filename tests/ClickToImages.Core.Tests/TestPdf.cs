using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClickToImages.Core.Tests;

// Writes a minimal valid PDF so the render tests run against the real Windows.Data.Pdf without a
// binary fixture in the repo. Each page is a MediaBox in points and an optional /Rotate. By default
// every page draws a black 50pt square at its bottom-left corner (so the top-right corner is always
// background); WriteCheckerboard fills the page with 2pt squares, detail that JPEG can't flatten.
internal static class TestPdf
{
    public static void Write(string path, params (int WidthPt, int HeightPt, int Rotate)[] pages)
    {
        WritePages(path, "0 0 0 rg 0 0 50 50 re f", pages);
    }

    public static void WriteCheckerboard(string path, int widthPt, int heightPt)
    {
        var squares = new StringBuilder("0 0 0 rg");
        for (int y = 0; y < heightPt; y += 2)
        {
            for (int x = (y / 2) % 2 * 2; x < widthPt; x += 4)
            {
                squares.Append(CultureInfo.InvariantCulture, $" {x} {y} 2 2 re");
            }
        }

        WritePages(path, squares.Append(" f").ToString(), [(widthPt, heightPt, 0)]);
    }

    /// <summary>
    /// Like <see cref="Write"/>, but encrypted with the PDF standard security handler: RC4, 128-bit
    /// key, revision 3 (ISO 32000-1 §7.6.3, algorithms 1, 2, 3 and 5). Windows.Data.Pdf opens this
    /// kind, unlike AES-256 (spec §4.2). An empty <paramref name="userPassword"/> gives a PDF any
    /// viewer opens silently, the permissions-only case.
    /// </summary>
    public static void WriteEncrypted(
        string path, string userPassword, string ownerPassword, params (int WidthPt, int HeightPt, int Rotate)[] pages)
    {
        byte[] fileId = MD5.HashData(Encoding.ASCII.GetBytes(path));
        const int permissions = -4;
        byte[] owner = OwnerValue(userPassword, ownerPassword);
        byte[] key = EncryptionKey(userPassword, owner, permissions, fileId);
        byte[] user = UserValue(key, fileId);
        var encryption = new Encryption(
            key,
            $"<< /Filter /Standard /V 2 /R 3 /Length 128 /P {permissions} /O <{Convert.ToHexString(owner)}> " +
            $"/U <{Convert.ToHexString(user)}> >>",
            $"/ID [<{Convert.ToHexString(fileId)}> <{Convert.ToHexString(fileId)}>]");
        WritePages(path, "0 0 0 rg 0 0 50 50 re f", pages, encryption);
    }

    private sealed record Encryption(byte[] Key, string Dictionary, string TrailerId);

    private static readonly byte[] PasswordPadding = Convert.FromHexString(
        "28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");

    private static byte[] Padded(string password) =>
        [.. Encoding.ASCII.GetBytes(password).Concat(PasswordPadding).Take(32)];

    // Algorithm 3.
    private static byte[] OwnerValue(string userPassword, string ownerPassword)
    {
        byte[] hash = MD5.HashData(Padded(ownerPassword));
        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash);
        }

        return Rc4Rounds(hash, Padded(userPassword));
    }

    // Algorithm 2.
    private static byte[] EncryptionKey(string userPassword, byte[] owner, int permissions, byte[] fileId)
    {
        byte[] hash = MD5.HashData([.. Padded(userPassword), .. owner, .. BitConverter.GetBytes(permissions), .. fileId]);
        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash);
        }

        return hash;
    }

    // Algorithm 5: the last 16 bytes are arbitrary padding.
    private static byte[] UserValue(byte[] key, byte[] fileId) =>
        [.. Rc4Rounds(key, MD5.HashData([.. PasswordPadding, .. fileId])), .. new byte[16]];

    // RC4 with the key, then 19 more passes with each key byte XORed with the pass number.
    private static byte[] Rc4Rounds(byte[] key, byte[] data)
    {
        byte[] result = Rc4(key, data);
        for (byte pass = 1; pass <= 19; pass++)
        {
            result = Rc4([.. key.Select(b => (byte)(b ^ pass))], result);
        }

        return result;
    }

    // Algorithm 1: each object's strings and streams under a key mixed with its number.
    private static byte[] ObjectKey(byte[] key, int objectNumber) =>
        [.. MD5.HashData([.. key, (byte)objectNumber, (byte)(objectNumber >> 8), (byte)(objectNumber >> 16), 0, 0])];

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        byte[] state = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            output[n] = (byte)(data[n] ^ state[(state[i] + state[j]) & 0xFF]);
        }

        return output;
    }

    private static void WritePages(
        string path, string content, (int WidthPt, int HeightPt, int Rotate)[] pages, Encryption? encryption = null)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Length} /Kids [{string.Join(" ", pages.Select((_, i) => $"{3 + (i * 2)} 0 R"))}] >>",
        };
        for (int i = 0; i < pages.Length; i++)
        {
            (int width, int height, int rotate) = pages[i];
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Rotate {rotate} /Contents {4 + (i * 2)} 0 R >>");
            // Latin-1 throughout, so encrypted bytes survive the string round trip one char per byte.
            string stream = encryption is null
                ? content
                : Encoding.Latin1.GetString(Rc4(ObjectKey(encryption.Key, 4 + (i * 2)), Encoding.ASCII.GetBytes(content)));
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
        }

        string trailerExtra = "";
        if (encryption is not null)
        {
            objects.Add(encryption.Dictionary);
            trailerExtra = $" /Encrypt {objects.Count} 0 R {encryption.TrailerId}";
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xrefOffset = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{trailerExtra} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.Latin1);
    }
}
