using ClickToImages.App.State;
using Windows.Foundation;

namespace ClickToImages.App.Tests;

public class RotatedFitTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    public void Scale_NotQuarterTurned_LeavesTheUniformFitAlone(int rotationDegrees)
    {
        Assert.Equal(1.0, RotatedFit.Scale(new Size(100, 200), new Size(100, 200), rotationDegrees));
    }

    // A portrait page filling a portrait area, turned landscape, has to halve to fit its width.
    // The image's own pixel size must not matter, only its shape: Stretch="Uniform" has already
    // fitted it to the area.
    [Theory]
    [InlineData(100, 200)]
    [InlineData(1000, 2000)]
    public void Scale_PortraitTurnedLandscape_ShrinksToTheAreaWidth(double imageWidth, double imageHeight)
    {
        Assert.Equal(0.5, RotatedFit.Scale(new Size(100, 200), new Size(imageWidth, imageHeight), 90), 6);
    }

    // A landscape page in a tall tile fits by width with room to spare; turned portrait it can grow
    // until it reaches the tile's height.
    [Fact]
    public void Scale_LandscapeTurnedPortraitInATallArea_Grows()
    {
        double scale = RotatedFit.Scale(new Size(158, 184), new Size(200, 100), 270);

        Assert.Equal(184.0 / 158.0, scale, 6);
    }

    [Fact]
    public void Scale_NoImageYet_IsOne()
    {
        Assert.Equal(1.0, RotatedFit.Scale(new Size(158, 184), new Size(0, 0), 90));
    }
}
