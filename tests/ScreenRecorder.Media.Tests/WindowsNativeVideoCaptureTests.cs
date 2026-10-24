// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.Windows.Capture;

namespace ScreenRecorder.Media.Tests;

public class WindowsNativeVideoCaptureTests
{
    private static readonly CaptureRegion Bounds = new(-1800, -100, 640, 480);
    private static readonly GraphicsCaptureMonitor Monitor = new(42, "LEFT", new(-1920, -200, 1920, 1080));
    private static readonly CaptureSelection Selection = new(CaptureBackend.WindowsGraphicsCapture,
        CaptureFallbackReason.None, "LEFT", null, null, Bounds) { OutputBounds = Bounds };

    [Fact]
    public void HelperArgumentsUsePhysicalCropRelativeToNegativeOrigin()
    {
        var launch = WindowsNativeVideoCapture.BuildStartInfo("owned-helper.exe", new() { Fps = 60 }, Bounds, Monitor);
        Assert.Equal(new[] { "42", "120", "100", "640", "480", "1920", "1080", "60" }, launch.Start.ArgumentList);
        Assert.True(launch.Start.RedirectStandardInput && launch.Start.RedirectStandardOutput && launch.Start.RedirectStandardError);
        Assert.False(launch.Start.UseShellExecute);
        Assert.Equal(Selection, launch.Selection);
    }

    [Theory]
    [InlineData(641, 480, 60)]
    [InlineData(640, 481, 60)]
    [InlineData(640, 480, 0)]
    [InlineData(640, 480, 121)]
    [InlineData(1920, 1080, 60)]
    public void InvalidGeometryOrRateCannotLaunchHelper(int width, int height, int fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsNativeVideoCapture.BuildStartInfo("helper", new() { Fps = fps },
            Bounds with { Width = width, Height = height }, Monitor));
    }

    [Fact]
    public void ResumeCannotSwitchCaptureIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => WindowsNativeVideoCapture.BuildStartInfo("helper", new(), Bounds, Monitor,
            Selection with { DeviceName = "OTHER" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonitorResolutionUsesPhysicalDpiContextAndRestoresCallingThread(bool fail)
    {
        nint context = -2;
        nint Set(nint value) { var previous = context; context = value; return previous; }
        GraphicsCaptureMonitor? Resolve(CaptureRegion bounds)
        {
            Assert.Equal((nint)(-4), context);
            if (fail) throw new IOException("Display query failed.");
            return Monitor;
        }
        if (fail) Assert.Throws<IOException>(() => WindowsNativeVideoCapture.ResolvePhysicalMonitor(Bounds, Resolve, Set));
        else Assert.Equal(Monitor, WindowsNativeVideoCapture.ResolvePhysicalMonitor(Bounds, Resolve, Set));
        Assert.Equal((nint)(-2), context);
    }

    [Fact]
    public async Task OwnedHelperRoutesMetadataPrivatelyAndExposesBinaryVideo()
    {
        await using var capture = await WindowsNativeVideoCapture.PrepareProcessAsync(Child("valid"), Selection, default);
        var events = new List<NativePointerEvent>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.PointerMetadata += (_, value) => { events.Add(value); if (events.Count == 3) received.TrySetResult(); };
        await capture.BeginAsync(default);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bytes = new byte[4];
        await capture.Output.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 0x42, 0x47, 0x52, 0x41 }, bytes);
        Assert.Equal(new NativeFrameClock(0, 1000, 999, 1), events[0].Frame);
        Assert.Equal(1, events[1].Pointer!.Value.Buttons);
        Assert.Equal(0, events[2].Pointer!.Value.Buttons);
        var firstStop = capture.StopAsync();
        Assert.Same(firstStop, capture.StopAsync());
        await firstStop.WaitAsync(TimeSpan.FromSeconds(8));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("overflow")]
    [InlineData("oversized")]
    public async Task BadPointerTrackStopsMetadataButKeepsVideoAlive(string mode)
    {
        await using var capture = await WindowsNativeVideoCapture.PrepareProcessAsync(Child(mode), Selection, default);
        var incomplete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = 0;
        capture.PointerIncomplete += (_, reason) => incomplete.TrySetResult(reason);
        capture.PointerMetadata += (_, _) => Interlocked.Increment(ref metadata);
        await capture.BeginAsync(default);
        var reason = await incomplete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bytes = new byte[4];
        await capture.Output.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(1, metadata); // Only the valid frame clock before the failure.
        Assert.DoesNotContain("9999", reason);
        Assert.Equal(0x42, bytes[0]);
    }

    [Fact]
    public async Task InvalidReadinessNeverLeaksPrivateHelperText()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => WindowsNativeVideoCapture.PrepareProcessAsync(Child("bad-ready"), Selection, default));
        Assert.DoesNotContain("private-data", error.ToString());
    }

    [Fact]
    public async Task StopTerminatesUnresponsiveOwnedHelperWithinBound()
    {
        var capture = await WindowsNativeVideoCapture.PrepareProcessAsync(Child("ignore-stop"), Selection, default);
        await capture.BeginAsync(default);
        var bytes = new byte[4];
        await capture.Output.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        try { await Assert.ThrowsAsync<IOException>(() => capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(8))); }
        finally { try { await capture.DisposeAsync(); } catch (IOException) { } }
    }

    [Fact]
    public async Task CancelledReadinessReapsTheUnresponsiveOwnedHelper()
    {
        using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WindowsNativeVideoCapture.PrepareProcessAsync(Child("silent"), Selection, token.Token)
                .WaitAsync(TimeSpan.FromSeconds(8)));
    }

    [Theory]
    [InlineData("ERROR WGC frame acquisition failed", "WGC frame acquisition failed")]
    [InlineData("ERROR Native video transport failed", "Native video transport failed")]
    [InlineData("ERROR Windows capture initialization HRESULT=-2005270523", "HRESULT=-2005270523")]
    [InlineData("ERROR private-path-or-coordinates", "unclassified helper failure")]
    [InlineData("ERROR WGC frame acquisition failed private-data", "unclassified helper failure")]
    public void CaptureFailuresExposeOnlySafeDiagnosticStages(string record, string expected)
    {
        var error = WindowsNativeVideoCapture.SafeFailure(record);
        Assert.Contains(expected, error);
        Assert.DoesNotContain("private-", error);
    }

    private static System.Diagnostics.ProcessStartInfo Child(string mode)
    {
        var start = EncoderProbeRunnerTests.Child("windows-native-helper");
        start.RedirectStandardInput = true;
        start.ArgumentList.Add(mode);
        return start;
    }
}
