// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsRecordingEngineFactoryTests
{
    [Fact]
    public async Task ResumedLegacyNativeCaptureReceivesItsOriginalIdentityWithoutRouterCoreMarker()
    {
        var bounds = new CaptureRegion(0, 0, 640, 480);
        var original = new CaptureSelection(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None,
            "42", null, null, bounds) { OutputBounds = bounds };
        var provider = new NativeBoundary();
        var factory = new RecordingEngineFactory(provider, new FFmpegEncoderDetector(provider));
        await using var engine = factory.Create(HardwareEncoderType.SoftwareCpu, original with { RecordingCore = "ffmpeg" });
        using var output = new MemoryStream();
        ((IBoundRecordingOutput)engine).OutputStream = output;
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync("unused",
            new() { NativeCaptureEnabled = true }, bounds));
        Assert.Equal(original, provider.Observed);
    }

    [Fact]
    public async Task ResumedCompatibilityMarkerDoesNotReenterTheNativeCaptureProvider()
    {
        var bounds = new CaptureRegion(0, 0, 640, 480);
        var pinned = new CaptureSelection(CaptureBackend.PlatformDefault, CaptureFallbackReason.StartupFailed,
            null, null, null, bounds) { RecordingCore = "ffmpeg" };
        var provider = new NativeBoundary();
        await using var engine = new RecordingEngineFactory(provider, new FFmpegEncoderDetector(provider))
            .Create(HardwareEncoderType.SoftwareCpu, pinned);
        using var output = new MemoryStream();
        ((IBoundRecordingOutput)engine).OutputStream = output;
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync("unused", new() { NativeCaptureEnabled = true }, bounds));
        Assert.Null(provider.Observed);
        Assert.Equal(1, provider.CompatibilityInputs);
    }

    private sealed class NativeBoundary : IFFmpegPlatformProvider, INativeVideoPlatformProvider
    {
        public CaptureSelection? Observed { get; private set; }
        public int CompatibilityInputs { get; private set; }
        public HardwareEncoderType? RequiredNativeEncoder => HardwareEncoderType.SoftwareCpu;
        public Task<INativeVideoCapture> PrepareNativeVideoAsync(RecordingConfiguration config, CaptureRegion bounds,
            CaptureSelection? pinned, CancellationToken token)
        {
            Observed = pinned;
            throw new InvalidOperationException("Stop at the native capture boundary; no hardware is opened.");
        }
        public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes() => [];
        public string BuildInputArguments(RecordingConfiguration config, int x, int y, int width, int height, bool synthetic,
            bool microphone, string? system = null, string? mic = null)
        {
            CompatibilityInputs++;
            throw new NotSupportedException("Stop at the compatibility launch boundary; no capture is opened.");
        }
        public string BuildOutputArguments(RecordingConfiguration config, HardwareEncoderType encoder, string path) => throw new NotSupportedException();
        public string BuildNativeInput(RecordingConfiguration config, string video, bool mic, string? system, string? microphone) => throw new NotSupportedException();
        public string BuildNativeOutput(RecordingConfiguration config, string path) => throw new NotSupportedException();
    }
}
