using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsDisplayServiceTests
{
    [Fact]
    public void GetMonitors_UsesNativePixelsAndZeroBasedIndices()
    {
        var api = new FakeDisplayApi(
            new MacOsDisplaySnapshot(
                42, 0, 0, 2240, 1260, 4480, 2520, true));
        var service = new MacOsDisplayService(api);

        var monitor = Assert.Single(service.GetMonitors());

        Assert.Equal(0, monitor.Index);
        Assert.Equal(4480, monitor.Bounds.Width);
        Assert.Equal(2520, monitor.Bounds.Height);
        Assert.Equal(2.0, monitor.DpiScaling, 3);
        Assert.True(monitor.IsPrimary);
    }

    [Fact]
    public void GetMonitors_WithNoNativeDisplays_UsesSafeFallback()
    {
        var service = new MacOsDisplayService(new FakeDisplayApi());

        var monitor = Assert.Single(service.GetMonitors());

        Assert.Equal(0, monitor.Index);
        Assert.Equal(1920, monitor.Bounds.Width);
        Assert.Equal(1080, monitor.Bounds.Height);
    }

    [Fact]
    public void GetNativeDisplayId_UsesTheSameStableEnumerationIndex()
    {
        var api = new FakeDisplayApi(
            new MacOsDisplaySnapshot(
                91, 0, 0, 1120, 630, 2240, 1260, true),
            new MacOsDisplaySnapshot(
                42, 1120, 0, 1920, 1080, 1920, 1080, false));
        var service = new MacOsDisplayService(api);

        Assert.Equal((uint)42, service.GetNativeDisplayId(1));
        Assert.Equal((uint)91, service.GetNativeDisplayId(99));
    }

    private sealed class FakeDisplayApi(
        params MacOsDisplaySnapshot[] displays) : IMacOsDisplayApi
    {
        public IReadOnlyList<MacOsDisplaySnapshot> GetActiveDisplays() => displays;
    }
}
