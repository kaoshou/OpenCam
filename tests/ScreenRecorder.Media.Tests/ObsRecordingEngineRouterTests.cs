// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsRecordingEngineRouterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencam-obs-route-" + Guid.NewGuid().ToString("N"));
    private static readonly MonitorInfo Display = new(4, "friendly display label", new(-640, 0, 640, 480), true, 2);
    private const string NativeId = "55443322-1122-3344-5566-778899001122";

    public ObsRecordingEngineRouterTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task StrictNativeModeRejectsMissingRuntimeWithoutWritingLegacyOutput()
    {
        using var output = new MemoryStream();
        await using var engine = new ObsRecordingEngineRouter(null, () => new LegacyEngine(),
            (_, _, _) => Display, HardwareEncoderType.SoftwareCpu, requireNative: true) { OutputStream = output };
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Display.Bounds));
        Assert.False(engine.IsRunning);
        Assert.Equal(0, output.Length);
        Assert.Null(engine.ActiveCapture);
    }

    [MacOsOnlyFact]
    public async Task StrictNativeModeRejectsChangedRuntimeInsteadOfCreatingCompatibilityRecording()
    {
        var runtime = WriteRuntime();
        await File.AppendAllTextAsync(runtime.HelperPath, "changed");
        using var output = new MemoryStream();
        await using var engine = new ObsRecordingEngineRouter(runtime, () => new LegacyEngine(),
            (_, _, _) => Display with { DeviceName = NativeId }, HardwareEncoderType.SoftwareCpu,
            requireNative: true) { OutputStream = output };
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Display.Bounds));
        Assert.False(engine.IsRunning);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void ResolvesExactIdentityAndRejectsTopologyIndexGuessingOrMissingIdentity()
    {
        var config = new RecordingConfiguration { MonitorIndex = 4 };
        var monitor = ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, null, [Display], _ => NativeId);
        Assert.Equal(NativeId, monitor?.DeviceName);
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(config, new(-100, 0, 640, 480), null, [Display], _ => NativeId));
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, null, [Display], _ => null));
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(new() { MonitorIndex = 0 }, Display.Bounds, null, [Display], _ => NativeId));
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, null, [Display, Display with { Index = 5 }], _ => NativeId));
    }

    [Fact]
    public void ResumeUsesPinnedNativeIdentityInsteadOfReassignedUiIndex()
    {
        var pinned = new CaptureSelection(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None,
            NativeId, null, null, Display.Bounds) { RecordingCore = "libobs-v1", OutputBounds = Display.Bounds };
        var config = new RecordingConfiguration { MonitorIndex = 0 };
        Assert.NotNull(ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, pinned,
            [Display with { Index = 9 }], _ => NativeId));
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, pinned,
            [Display], _ => "different-native-display"));
        Assert.Null(ObsRecordingEngineRouter.ResolveMonitor(config, Display.Bounds, pinned,
            [Display with { Bounds = new(-640, 0, 1280, 960) }], _ => NativeId));
    }

    [MacOsOnlyFact]
    public async Task ExplicitMacStandardUsesLegacyEvenWhenValidatedNativeRuntimeIsAvailable()
    {
        var runtime = WriteRuntime();
        using var output = new MemoryStream();
        await using var engine = Router(runtime);
        engine.OutputStream = output;
        var config = Config();
        config.NativeCaptureEnabled = false;
        await engine.StartRecordingAsync("unused", config, Display.Bounds);
        Assert.Equal("ffmpeg", engine.ActiveCapture?.RecordingCore);
        Assert.Equal("legacy", Encoding.ASCII.GetString(output.ToArray()));
    }

    [MacOsOnlyFact]
    public async Task PinnedNativeSessionRejectsMacStandardPreferenceWithoutWritingOrSwitchingCore()
    {
        var runtime = WriteRuntime();
        var pinned = new CaptureSelection(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None,
            NativeId, null, null, Display.Bounds) { RecordingCore = "libobs-v1", OutputBounds = Display.Bounds };
        using var output = new MemoryStream();
        await using var engine = Router(runtime, pinned);
        engine.OutputStream = output;
        var config = Config();
        config.NativeCaptureEnabled = false;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", config, Display.Bounds));
        Assert.Equal(0, output.Length);
        Assert.Null(engine.ActiveCapture);
    }

    [MacOsOnlyFact]
    public async Task SelectsValidatedCapableRuntimeAndForwardsOutputAndMetrics()
    {
        var runtime = WriteRuntime();
        using var output = new MemoryStream();
        await using var engine = Router(runtime);
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Display.Bounds);
        Assert.Equal("libobs-v1", engine.ActiveCapture?.RecordingCore);
        await engine.StopRecordingAsync();
        Assert.Equal("native-mkv", Encoding.ASCII.GetString(output.ToArray()));
    }

    [MacOsOnlyFact]
    public async Task PreservesLegacyCaptureForMissingCapabilitiesAndEditablePointer()
    {
        var runtime = WriteRuntime(audio: ["none"]);
        using var output = new MemoryStream();
        await using (var engine = Router(runtime))
        {
            engine.OutputStream = output;
            await engine.StartRecordingAsync("unused", Config(), Display.Bounds);
            Assert.Equal("ffmpeg", engine.ActiveCapture?.RecordingCore);
            Assert.Equal("legacy", Encoding.ASCII.GetString(output.ToArray()));
        }
        runtime = WriteRuntime();
        await using (var engine = Router(runtime))
        {
            var config = Config();
            config.EditablePointerEnabled = true;
            engine.OutputStream = output;
            await engine.StartRecordingAsync("unused", config, Display.Bounds);
            Assert.Equal("ffmpeg", engine.ActiveCapture?.RecordingCore);
        }
    }

    [MacOsOnlyFact]
    public async Task RuntimeChangesBeforeLaunchFallbackOnlyForNewSession()
    {
        var runtime = WriteRuntime();
        await File.AppendAllTextAsync(runtime.HelperPath, "\n# changed\n");
        using var output = new MemoryStream();
        await using (var engine = Router(runtime))
        {
            engine.OutputStream = output;
            await engine.StartRecordingAsync("unused", Config(), Display.Bounds);
            Assert.Equal("legacy", Encoding.ASCII.GetString(output.ToArray()));
        }
        var pinned = new CaptureSelection(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None, NativeId, null, null, Display.Bounds)
            { RecordingCore = "libobs-v1", OutputBounds = Display.Bounds };
        await using (var engine = Router(runtime, pinned))
        {
            engine.OutputStream = output;
            await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Display.Bounds));
        }
    }

    [MacOsOnlyFact]
    public async Task FailedNativeStartupDoesNotSwitchCoreOrDevice()
    {
        var runtime = WriteRuntime(fail: true);
        using var output = new MemoryStream();
        await using var engine = Router(runtime);
        engine.OutputStream = output;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Display.Bounds));
        Assert.DoesNotContain("legacy", Encoding.ASCII.GetString(output.ToArray()));
    }

    [MacOsOnlyFact]
    public async Task ExistingLegacySessionDoesNotSwitchWhenRuntimeBecomesAvailable()
    {
        var runtime = WriteRuntime();
        using var output = new MemoryStream();
        var pinned = new CaptureSelection(CaptureBackend.PlatformDefault, CaptureFallbackReason.None, null, null, null, Display.Bounds)
            { RecordingCore = "ffmpeg" };
        await using var engine = Router(runtime, pinned);
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Display.Bounds);
        Assert.Equal("ffmpeg", engine.ActiveCapture?.RecordingCore);
        Assert.Equal("legacy", Encoding.ASCII.GetString(output.ToArray()));
    }

    [MacOsOnlyFact]
    public async Task CompatibilityFallbackReasonSurvivesPauseAndResumeWhenLegacyEngineHasNoSelection()
    {
        var runtime = WriteRuntime();
        var fallback = new CaptureSelection(CaptureBackend.PlatformDefault, CaptureFallbackReason.StartupFailed,
            null, null, null, Display.Bounds);
        using var output = new MemoryStream();
        CaptureSelection resumed;
        await using (var first = Router(runtime, fallback))
        {
            first.OutputStream = output;
            await first.StartRecordingAsync("unused", Config(), Display.Bounds);
            await first.StopRecordingAsync();
            resumed = Assert.IsType<CaptureSelection>(first.ActiveCapture);
            Assert.Equal(CaptureFallbackReason.StartupFailed, resumed.FallbackReason);
            Assert.Equal("ffmpeg", resumed.RecordingCore);
        }
        await using var next = Router(runtime, resumed);
        next.OutputStream = output;
        await next.StartRecordingAsync("unused", Config(), Display.Bounds);
        Assert.Equal(fallback with { RecordingCore = "ffmpeg" }, next.ActiveCapture);
    }

    private ObsRecordingEngineRouter Router(ObsRuntime runtime, CaptureSelection? pinned = null) => new(runtime,
        () => new LegacyEngine(), (config, bounds, capture) => ObsRecordingEngineRouter.ResolveMonitor(config, bounds, capture,
            [Display], _ => NativeId), HardwareEncoderType.SoftwareCpu, pinned);

    private static RecordingConfiguration Config() => new() { MonitorIndex = 4, Fps = 30, AudioSource = AudioSourceType.SystemOnly, NativeCaptureEnabled = true };

    private ObsRuntime WriteRuntime(string[]? audio = null, bool fail = false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Contents", "MacOS"));
        var helper = Path.Combine(_root, "Contents", "MacOS", "OpenCam.ObsCapture");
        File.WriteAllText(helper, "#!/bin/sh\nIFS= read -r request\nprintf 'native-mkv'\n" + (fail ? "exit 7\n" :
            "printf '%s\\n' 'OCOBS {\"event\":\"ready\",\"protocol\":1,\"backend\":\"sck\",\"encoder\":\"cpu\"}' >&2\n" +
            "while IFS= read -r line; do [ \"$line\" = STOP ] && break; done\n" +
            "printf '%s\\n' 'OCOBS {\"event\":\"stopped\"}' >&2\n"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(_root, "runtime.json"), JsonSerializer.Serialize(new
        {
            protocol = 1, enabled = true, obsVersion = "33.0.0", platform = "osx-arm64", sourceRevision = "72e86655715d9a25d69ce446aa4dd44ba58e10d2",
            files = new Dictionary<string, string> { ["Contents/MacOS/OpenCam.ObsCapture"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper))) },
            capabilities = new { backends = new[] { "sck" }, audio = audio ?? ["none", "system"], encoders = new[] { "cpu" } }
        }));
        return ObsRuntime.TryLoad(_root, "osx-arm64")!;
    }

    private sealed class LegacyEngine : IScreenRecorderEngine, IBoundRecordingOutput
    {
        public bool IsRunning { get; private set; }
        public bool UseSyntheticCaptureSource { get; set; }
        public TimeSpan CurrentRecordedTime => TimeSpan.Zero;
        public long CurrentFramesRecorded => 0;
        public HardwareEncoderType ActiveEncoder => HardwareEncoderType.SoftwareCpu;
        public CaptureSelection? ActiveCapture => null;
        public Stream? OutputStream { get; set; }
        public event EventHandler<string>? EngineErrorOccurred { add { } remove { } }
        public event EventHandler<string>? EngineWarningOccurred { add { } remove { } }
        public event EventHandler? AudioDeviceLost { add { } remove { } }
        public async Task StartRecordingAsync(string path, RecordingConfiguration config, CaptureRegion bounds, CancellationToken token = default)
        {
            await OutputStream!.WriteAsync("legacy"u8.ToArray(), token);
            IsRunning = true;
        }
        public Task StopRecordingAsync(CancellationToken token = default) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
