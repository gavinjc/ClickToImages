using Windows.Foundation;

namespace ClickToImages.App.State;

// Stretch="Uniform" fits a page image to its area before the image's RotateTransform applies, so a
// quarter-turned page overflows: a portrait page turned landscape is too wide. This is the extra
// scale that makes the turned page fit instead. WinUI has no LayoutTransform to do it for us, and
// resizing the Image doesn't work: an Image bigger than its slot is clipped before it turns.
public static class RotatedFit
{
    public static double Scale(Size area, Size image, int rotationDegrees)
    {
        if (rotationDegrees % 180 == 0 || area.Width <= 0 || area.Height <= 0 || image.Width <= 0 || image.Height <= 0)
        {
            return 1.0;
        }

        double uniformFit = Math.Min(area.Width / image.Width, area.Height / image.Height);
        double fittedWidth = image.Width * uniformFit;
        double fittedHeight = image.Height * uniformFit;
        return Math.Min(area.Width / fittedHeight, area.Height / fittedWidth);
    }
}
