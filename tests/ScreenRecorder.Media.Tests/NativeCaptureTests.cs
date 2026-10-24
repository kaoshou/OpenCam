// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Diagnostics;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows.Capture;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.UI.ViewModels;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class NativeCaptureTests
{
    [Theory]
    [InlineData(AudioSourceType.None)]
    [InlineData(AudioSourceType.SystemOnly)]
    [InlineData(AudioSourceType.MicrophoneOnly)]
    [InlineData(AudioSourceType.SystemAndMicrophone)]
    public void WgcNvencDoesNotWaitForFutureDesktopUpdatesToReleaseEncodedFrames(AudioSourceType audio)
    {
        var provider = new WindowsFFmpegProvider();
        var config = new RecordingConfiguration { NativeCaptureEnabled = true, AudioSource = audio };
        var args = provider.BuildOutputArguments(config, HardwareEncoderType.NvidiaNvenc, "pipe:1");
        Assert.Contains("-c:v h264_nvenc -preset p4 -cq 23", args);
        Assert.Contains("-delay 0", args);
        Assert.Contains("-bf 0", args);
        Assert.Contains("-rc-lookahead 0", args);
        Assert.Contains("-zerolatency 1", args);
        Assert.DoesNotContain("-tune zerolatency", args); // x264-only syntax
        Assert.Contains("-cluster_time_limit 1000", args);
        Assert.Equal(audio != AudioSourceType.None, args.Contains("-c:a aac"));

        config.NativeCaptureEnabled = false;
        var standard = provider.BuildOutputArguments(config, HardwareEncoderType.NvidiaNvenc, "pipe:1");
        Assert.DoesNotContain("-delay 0", standard);
        Assert.DoesNotContain("-zerolatency", standard);
        var cpu = provider.BuildOutputArguments(config, HardwareEncoderType.SoftwareCpu, "pipe:1");
        Assert.DoesNotContain("-delay", cpu);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task WgcRequiresCompleteCapabilityHelp(bool complete, bool expected)
    {
        var help = complete ? "Filter gfxcapture hmonitor max_framerate capture_cursor crop_left crop_top crop_right crop_bottom width height output_fmt" : "Unknown filter gfxcapture";
        Assert.Equal(expected, await WindowsGraphicsCapturePlan.ProbeAsync("ffmpeg", default,
            new Probe(new(EncoderProbeStatus.Available, 0, TimeSpan.Zero, help, "", true))));
    }

    [Fact]
    public async Task WgcRejectsUnconfirmedProbeCleanupAndCancellation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => WindowsGraphicsCapturePlan.ProbeAsync("ffmpeg", default,
            new Probe(new(EncoderProbeStatus.Available, 0, TimeSpan.Zero, "", "", false))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WindowsGraphicsCapturePlan.ProbeAsync("ffmpeg", default,
            new Probe(new(EncoderProbeStatus.Cancelled, null, TimeSpan.Zero, "", "", true))));
    }

    private sealed class Probe(EncoderProbeResult result) : IEncoderProbeRunner
    {
        public Task<EncoderProbeResult> RunAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken token = default)
        {
            Assert.Contains("filter=gfxcapture", start.ArgumentList);
            return Task.FromResult(result);
        }
    }

    [MacOsOnlyFact]
    public async Task NativeEncodedPipeMuxesAudioAndReapsProducerOnStop()
    {
        var path = Path.Combine(Path.GetTempPath(), $"native-mux-{Guid.NewGuid():N}.mkv");
        var provider = new SyntheticNativeProvider();
        await using var engine = new FFmpegScreenRecorderEngine(provider);
        try
        {
            await engine.StartRecordingAsync(path, new() { NativeCaptureEnabled = true,
                AudioSource = AudioSourceType.None, MaintainSegmentAudioTrack = true, Fps = 30 }, new(0, 0, 320, 240));
            Assert.Equal(CaptureBackend.ScreenCaptureKit, engine.ActiveCapture!.Backend);
            await Task.Delay(300);
            await engine.StopRecordingAsync();
            Assert.True(provider.Capture!.Stopped);
            Assert.False(engine.IsRunning);
            var media = await new MediaFileProbe().ProbeAsync(path);
            Assert.True(media.IsValid);
            Assert.Equal(1, media.VideoStreamCount);
            Assert.Equal(1, media.AudioStreamCount);
            Assert.True(media.Duration.TotalSeconds > 0, media.RawJsonOutput);
        }
        finally { await engine.StopRecordingAsync(); File.Delete(path); }
    }

    [MacOsOnlyFact]
    public async Task NativeVideoFailureCannotLeaveAudioOnlyMuxerRunning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"native-failure-{Guid.NewGuid():N}.mkv");
        var provider = new SyntheticNativeProvider();
        await using var engine = new FFmpegScreenRecorderEngine(provider);
        try
        {
            await engine.StartRecordingAsync(path, new() { NativeCaptureEnabled = true,
                AudioSource = AudioSourceType.None, MaintainSegmentAudioTrack = true }, new(0, 0, 320, 240));
            provider.Capture!.Fail();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (engine.IsRunning) await Task.Delay(10, deadline.Token);
            await engine.StopRecordingAsync();
            Assert.True(provider.Capture.Stopped);
        }
        finally { await engine.StopRecordingAsync(); File.Delete(path); }
    }

    private sealed class SyntheticNativeProvider : MacOsFFmpegProvider, INativeVideoPlatformProvider
    {
        public SyntheticNativeCapture? Capture { get; private set; }
        Task<INativeVideoCapture> INativeVideoPlatformProvider.PrepareNativeVideoAsync(RecordingConfiguration config,
            CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Capture = new SyntheticNativeCapture(bounds);
            return Task.FromResult<INativeVideoCapture>(Capture);
        }
    }

    private sealed class SyntheticNativeCapture : INativeVideoCapture
    {
        private readonly Process _process;
        private readonly Task<string> _stderr;
        private readonly object _stopLock = new();
        private Task? _stop;
        public SyntheticNativeCapture(CaptureRegion bounds)
        {
            Selection = new(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None, "synthetic", null, null, bounds);
            _process = Process.Start(new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
            {
                Arguments = "-v error -re -f lavfi -i testsrc2=size=320x240:rate=30 -c:v libx264 -preset ultrafast -tune zerolatency -bf 0 -flush_packets 1 -cluster_time_limit 100 -f matroska pipe:1",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true
            })!;
            _stderr = _process.StandardError.ReadToEndAsync();
        }
        public CaptureSelection Selection { get; }
        public Stream Output => _process.StandardOutput.BaseStream;
        public event EventHandler<string>? CaptureFailed;
        public bool Stopped { get; private set; }
        public Task BeginAsync(CancellationToken token) => Task.CompletedTask;
        public void Fail() => CaptureFailed?.Invoke(this, "Injected native producer failure");
        public Task StopAsync()
        {
            lock (_stopLock) return _stop ??= StopCoreAsync();
        }
        private async Task StopCoreAsync()
        {
            if (Stopped) return;
            if (!_process.HasExited) _process.Kill(true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _process.WaitForExitAsync(deadline.Token);
            await _stderr;
            _process.Dispose();
            Stopped = true;
        }
        public async ValueTask DisposeAsync() => await StopAsync();
    }

    [Theory]
    [InlineData(CursorEffectMode.Hidden, 0)]
    [InlineData(CursorEffectMode.PresentationStick, 0)]
    public void WgcUsesExactMonitorAndRelativeCrop(CursorEffectMode cursor, int shown)
    {
        var bounds = new CaptureRegion(-1800, 100, 640, 480);
        var monitor = new GraphicsCaptureMonitor(12345, "LEFT", new(-1920, 0, 1920, 1080));
        var plan = WindowsGraphicsCapturePlan.Build(new() { Fps = 60, CursorEffect = cursor }, bounds, monitor);
        Assert.Equal(CaptureBackend.WindowsGraphicsCapture, plan.Selection.Backend);
        Assert.Contains("hmonitor=12345:max_framerate=60", plan.VideoInputArguments);
        Assert.Contains($"capture_cursor={shown}:crop_left=120:crop_top=100:crop_right=1160:crop_bottom=500", plan.VideoInputArguments);
        Assert.DoesNotContain("gdigrab", plan.VideoInputArguments);
        Assert.Throws<InvalidOperationException>(() => WindowsGraphicsCapturePlan.Build(new() { Fps = 60 }, bounds,
            monitor with { DeviceName = "OTHER" }, plan.Selection));
    }

    [Theory]
    [InlineData(-2000, 0, 640, 480)]
    [InlineData(-1800, 0, 641, 480)]
    [InlineData(-100, 0, 640, 480)]
    public void WgcRejectsInvalidOrCrossDisplayRegions(int x, int y, int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsGraphicsCapturePlan.Build(new(), new(x, y, width, height),
            new(1, "LEFT", new(-1920, 0, 1920, 1080))));

    [Theory]
    [InlineData(AudioSourceType.None)]
    [InlineData(AudioSourceType.SystemOnly)]
    [InlineData(AudioSourceType.MicrophoneOnly)]
    [InlineData(AudioSourceType.SystemAndMicrophone)]
    public void MacNativeVideoUsesCopyAndPreservesAudioSelection(AudioSourceType audio)
    {
        var provider = new MacOsFFmpegProvider();
        var config = new RecordingConfiguration { AudioSource = audio, NativeCaptureEnabled = true };
        var args = provider.BuildNativeInput(config, "-f matroska -i pipe:9", false,
            "-f s16le -ar 48000 -ac 2 -i pipe:7", "-f s16le -ar 48000 -ac 1 -i pipe:8");
        Assert.Contains("-f matroska -i pipe:9", args);
        Assert.DoesNotContain("Capture screen", args);
        Assert.DoesNotContain("-filter:v", args);
        Assert.Equal(audio is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone, args.Contains("pipe:7"));
        Assert.Equal(audio is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone, args.Contains("pipe:8"));
        var output = provider.BuildNativeOutput(config, "pipe:1");
        Assert.Contains("-c:v copy", output);
        Assert.Contains("-f matroska", output);
        Assert.Equal(audio != AudioSourceType.None, output.Contains("-c:a aac"));
    }

    [Fact]
    public async Task LegacyNativePreferenceDeserializesButCannotEnableWindowsWgc()
    {
        Assert.False(new UserSettings().NativeCaptureEnabled);
        var config = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(
            new RecordingConfiguration { NativeCaptureEnabled = true }));
        Assert.True(config!.NativeCaptureEnabled);
        var settings = new MockSettingsService();
        var vm = new SettingsViewModel(settings, true);
        await vm.LoadSettingsAsync();
        vm.NativeCaptureEnabled = true;
        await vm.SaveAsync();
        Assert.False(settings.CurrentSettings.NativeCaptureEnabled);
        vm.CanEditCaptureMode = false;
        vm.NativeCaptureEnabled = false;
        await vm.SaveAsync();
        Assert.False(settings.CurrentSettings.NativeCaptureEnabled);
    }
}
