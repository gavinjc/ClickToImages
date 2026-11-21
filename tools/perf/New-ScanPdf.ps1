<#
Writes a scan-like PDF for the spec §5 test set: A4 pages at 300 DPI, each one full-page JPEG
image of off-white paper with lines of text, as a copier makes. The number of lines is
calibrated on the first page so the file lands near -TargetMB. Never use real scans from Downloads (personal).

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\New-ScanPdf.ps1 -Path scan.pdf
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [int] $Pages = 200,
    [double] $TargetMB = 52
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;

public static class ScanPdfWriter
{
    private const int PageWidth = 2480;
    private const int PageHeight = 3508;
    private const int MaxLines = 58;

    private static readonly string[] Words =
    {
        "the", "contractor", "shall", "provide", "all", "materials", "and", "labour", "for",
        "works", "described", "in", "schedule", "drawings", "specification", "clause", "of",
        "this", "agreement", "including", "site", "access", "programme", "completion", "date",
        "payment", "retention", "variation", "notice", "within", "days", "written", "approval"
    };

    public static void Write(string path, int pages, long targetBytes, Action<string> report)
    {
        using (Bitmap coarse = MakeNoiseTile(64, 6, new Random(1)))
        using (Bitmap paper = new Bitmap(coarse, 512, 512))
        {
            // Text is what costs: at quality 50 an A4 300-DPI page is ~140 KB blank plus ~13 KB per
            // full line, and neither the quality nor added grain moves that much. So the line
            // count sets the size. The pixel count, which drives decode time, is fixed either way.
            int lines = LargestUnder(0, MaxLines, n => RenderPage(1, paper, n).Length, targetBytes / pages);
            report(lines + " lines of text per page");

            using (FileStream stream = File.Create(path))
            {
                var offsets = new List<long>();
                WriteAscii(stream, "%PDF-1.4\n");

                offsets.Add(stream.Position);
                WriteAscii(stream, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var kids = new StringBuilder();
                for (int i = 0; i < pages; i++)
                {
                    kids.Append(3 + 3 * i).Append(" 0 R ");
                }

                offsets.Add(stream.Position);
                WriteAscii(stream, "2 0 obj\n<< /Type /Pages /Count " + pages + " /Kids [" + kids + "] >>\nendobj\n");

                const string content = "q 595.28 0 0 841.89 0 0 cm /Im0 Do Q";
                for (int i = 0; i < pages; i++)
                {
                    int pageObject = 3 + 3 * i;
                    byte[] jpeg = RenderPage(i + 1, paper, lines);

                    offsets.Add(stream.Position);
                    WriteAscii(stream, pageObject + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] " +
                        "/Resources << /XObject << /Im0 " + (pageObject + 2) + " 0 R >> >> /Contents " +
                        (pageObject + 1) + " 0 R >>\nendobj\n");

                    offsets.Add(stream.Position);
                    WriteAscii(stream, (pageObject + 1) + " 0 obj\n<< /Length " + content.Length + " >>\nstream\n" +
                        content + "\nendstream\nendobj\n");

                    offsets.Add(stream.Position);
                    WriteAscii(stream, (pageObject + 2) + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + PageWidth +
                        " /Height " + PageHeight + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode " +
                        "/Length " + jpeg.Length + " >>\nstream\n");
                    stream.Write(jpeg, 0, jpeg.Length);
                    WriteAscii(stream, "\nendstream\nendobj\n");

                    if ((i + 1) % 20 == 0)
                    {
                        report("page " + (i + 1) + " of " + pages);
                    }
                }

                long xref = stream.Position;
                var table = new StringBuilder();
                table.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
                foreach (long offset in offsets)
                {
                    table.Append(offset.ToString("D10")).Append(" 00000 n \n");
                }

                table.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
                    .Append(xref).Append("\n%%EOF\n");
                WriteAscii(stream, table.ToString());
            }
        }
    }

    // The largest setting whose first page fits the target. Page sizes vary little, so one page
    // calibrates the whole file.
    private static int LargestUnder(int low, int high, Func<int, long> pageBytes, long targetPageBytes)
    {
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (pageBytes(middle) <= targetPageBytes)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static byte[] RenderPage(int pageNumber, Bitmap paper, int lines)
    {
        var random = new Random(pageNumber);
        using (var page = new Bitmap(PageWidth, PageHeight, PixelFormat.Format24bppRgb))
        {
            page.SetResolution(300, 300);
            using (Graphics graphics = Graphics.FromImage(page))
            using (var paperBrush = new TextureBrush(paper, WrapMode.Tile))
            using (var font = new Font("Times New Roman", 44, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.FromArgb(48, 46, 52)))
            {
                paperBrush.TranslateTransform(random.Next(512), random.Next(512));
                graphics.FillRectangle(paperBrush, 0, 0, PageWidth, PageHeight);

                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                graphics.RotateTransform((float)(random.NextDouble() - 0.5));
                graphics.DrawString("Page " + pageNumber, font, ink, 250, 200);
                for (int line = 0; line < lines; line++)
                {
                    var text = new StringBuilder();
                    while (text.Length < 90)
                    {
                        text.Append(Words[random.Next(Words.Length)]).Append(' ');
                    }

                    graphics.DrawString(text.ToString(), font, ink, 250, 330 + line * 52);
                }
            }

            ImageCodecInfo jpegCodec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.MimeType == "image/jpeg");
            using (var parameters = new EncoderParameters(1))
            using (var output = new MemoryStream())
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
                page.Save(output, jpegCodec, parameters);
                return output.ToArray();
            }
        }
    }

    // Blotchy paper tone, scaled up later so it is low-frequency like a real sheet.
    private static Bitmap MakeNoiseTile(int size, int amplitude, Random random)
    {
        var tile = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int grey = 238 + random.Next(-amplitude, amplitude / 2);
                tile.SetPixel(x, y, Color.FromArgb(grey, grey - 2, grey - 6));
            }
        }

        return tile;
    }

    private static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = Encoding.GetEncoding(28591).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
'@

$fullPath = [IO.Path]::GetFullPath($Path)
[ScanPdfWriter]::Write($fullPath, $Pages, [long]($TargetMB * 1MB), [Action[string]] { param($m) Write-Host $m })
$size = (Get-Item $fullPath).Length
Write-Host ("Wrote {0}: {1} pages, {2:N1} MB" -f $fullPath, $Pages, ($size / 1MB))
