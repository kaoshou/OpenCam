// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Windows.Display;
using ScreenRecorder.Platform.Windows.Audio;
using ScreenRecorder.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace ScreenRecorder.Media.Tests;

public class DisplayEnumerationTests
{
    private readonly ITestOutputHelper _output;

    public DisplayEnumerationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [WindowsOnlyFact]
    public void WindowsDisplayService_ShouldEnumerateMonitors()
    {
        var service = new WindowsDisplayService();
        var monitors = service.GetMonitors();

        Assert.NotEmpty(monitors);
        var primary = service.GetPrimaryMonitor();
        Assert.NotNull(primary);
        Assert.True(primary!.Bounds.Width > 0);
        Assert.True(primary.Bounds.Height > 0);

        _output.WriteLine($"[螢幕探測結果] 總共探測到 {monitors.Count} 個顯示器:");
        foreach (var m in monitors)
        {
            _output.WriteLine($"  - 螢幕 [{m.Index}]: {m.DeviceName}, 範圍={m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}, 主顯示器={m.IsPrimary}");
            Assert.True(m.Bounds.Width > 0);
            Assert.True(m.Bounds.Height > 0);
        }
    }

    [WindowsOnlyFact]
    public void WindowsDisplayService_VirtualScreenBounds_ShouldEncloseAllMonitors()
    {
        var service = new WindowsDisplayService();
        var monitors = service.GetMonitors();
        var virtualBounds = service.GetVirtualScreenBounds();

        Assert.NotEmpty(monitors);
        Assert.True(virtualBounds.Width > 0);
        Assert.True(virtualBounds.Height > 0);

        foreach (var m in monitors)
        {
            Assert.True(m.Bounds.X >= virtualBounds.X);
            Assert.True(m.Bounds.Y >= virtualBounds.Y);
            Assert.True(m.Bounds.X + m.Bounds.Width <= virtualBounds.X + virtualBounds.Width);
            Assert.True(m.Bounds.Y + m.Bounds.Height <= virtualBounds.Y + virtualBounds.Height);
        }

        _output.WriteLine($"[虛擬桌面邊界] {virtualBounds.X},{virtualBounds.Y} {virtualBounds.Width}x{virtualBounds.Height}");
    }

    [WindowsOnlyFact]
    public void WindowsAudioDeviceService_ShouldEnumerateRecordingDevices()
    {
        var service = new WindowsAudioDeviceService();
        var mics = service.GetRecordingDevices();

        Assert.NotEmpty(mics);
        _output.WriteLine($"[音訊輸入設備探測結果] 總共探測到 {mics.Count} 個輸入設備:");
        foreach (var mic in mics)
        {
            _output.WriteLine($"  - 麥克風: Id={mic.Id}, 名稱={mic.Name}, 預設={mic.IsDefault}");
            Assert.False(string.IsNullOrWhiteSpace(mic.Id));
            Assert.False(string.IsNullOrWhiteSpace(mic.Name));
            Assert.True(mic.IsInput);
        }
    }
}
