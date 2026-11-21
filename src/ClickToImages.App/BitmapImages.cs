using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ClickToImages.App;

// For the thumbnails and the preview overlay alike. BitmapImage keeps the encoded stream alive
// beside the pixels, so feed it something compressed (lessons.md → Images).
internal static class BitmapImages
{
    public static async Task<BitmapImage> FromEncodedAsync(byte[] encodedImage)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(encodedImage.AsBuffer());
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
