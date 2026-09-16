using Avalonia;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class RegionSelectionGeometryTests
{
    [Fact]
    public void FromWindow_ConvertsRetinaSizeToEvenPhysicalPixels()
    {
        var result = RegionSelectionGeometry.FromWindow(
            new PixelPoint(100, 80),
            new Size(641.5, 480.5),
            2.0);

        Assert.Equal(
            new CaptureRegion(100, 80, 1282, 960),
            result);
    }

    [Fact]
    public void ClampToBounds_ClampsToOneDisplay()
    {
        var result = RegionSelectionGeometry.ClampToBounds(
            new CaptureRegion(6300, 1000, 400, 300),
            new CaptureRegion(4480, 0, 1920, 1080));

        Assert.Equal(
            new CaptureRegion(6000, 780, 400, 300),
            result);
    }
}
