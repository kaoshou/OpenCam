// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public sealed class PreflightEncodingTests
{
    [Theory]
    [InlineData(false, AudioSourceType.None)]
    [InlineData(false, AudioSourceType.SystemOnly)]
    [InlineData(false, AudioSourceType.MicrophoneOnly)]
    [InlineData(false, AudioSourceType.SystemAndMicrophone)]
    [InlineData(true, AudioSourceType.None)]
    [InlineData(true, AudioSourceType.SystemOnly)]
    [InlineData(true, AudioSourceType.MicrophoneOnly)]
    [InlineData(true, AudioSourceType.SystemAndMicrophone)]
    public async Task RealLivePreviewProducesCompleteFramesForEverySelectedAudioMode(bool mac, AudioSourceType audio)
    {
        IFFmpegPlatformProvider provider = mac ? new MacOsFFmpegProvider(new TestDisplay()) : new WindowsFFmpegProvider();
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        var engine = new FFmpegScreenRecorderEngine(provider, ffmpegPath: executable,
            pinnedEncoder: HardwareEncoderType.SoftwareCpu, livePreviewOutput: true) { UseSyntheticCaptureSource = true };
        await using var live = new PreflightLiveSession(() => new(engine, []), new TestDisplay(), executable);
        var configuration = new RecordingConfiguration { AudioSource = audio, Fps = 30 };
        await live.StartAsync(configuration, default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ScreenRecorder.Infrastructure.IPC.ProjectFrameReply? frame;
        while ((frame = live.ReadFrame()) is null) await Task.Delay(20, deadline.Token);
        Assert.Equal(960, frame.PixelWidth);
        Assert.Equal(540, frame.PixelHeight);
        Assert.Equal(960 * 540 * 4, frame.Rgba!.Length);
        Assert.True(Enumerable.Range(0, frame.PixelWidth * frame.PixelHeight).Any(pixel =>
            frame.Rgba[pixel * 4] > 32 || frame.Rgba[pixel * 4 + 1] > 32 || frame.Rgba[pixel * 4 + 2] > 32),
            "Expected the synthetic picture, not an opaque black frame.");
        Assert.Equal(audio, configuration.AudioSource);
        Assert.Equal(HardwareEncoderType.SoftwareCpu, engine.ActiveEncoder);
        await live.StopAsync(default);
        Assert.False(engine.IsRunning);
        Assert.Null(live.ReadFrame());
    }

    private sealed class TestDisplay : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "synthetic", new(0, 0, 128, 72), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 128, 72);
    }

    [Theory]
    [InlineData(false, AudioSourceType.None)]
    [InlineData(false, AudioSourceType.SystemOnly)]
    [InlineData(false, AudioSourceType.MicrophoneOnly)]
    [InlineData(false, AudioSourceType.SystemAndMicrophone)]
    [InlineData(true, AudioSourceType.None)]
    [InlineData(true, AudioSourceType.SystemOnly)]
    [InlineData(true, AudioSourceType.MicrophoneOnly)]
    [InlineData(true, AudioSourceType.SystemAndMicrophone)]
    public void LivePreviewProfileReducesBufferingWithoutChangingSavedRecording(bool mac, AudioSourceType audio)
    {
        IFFmpegPlatformProvider provider = mac ? new MacOsFFmpegProvider() : new WindowsFFmpegProvider();
        var config = new RecordingConfiguration { AudioSource = audio };
        var recording = provider.BuildOutputArguments(config, HardwareEncoderType.SoftwareCpu, "pipe:1");
        var preview = provider.BuildLivePreviewOutputArguments(config, HardwareEncoderType.SoftwareCpu, "pipe:1");
        Assert.Contains("-tune zerolatency -bf 0", preview);
        Assert.Contains("-cluster_time_limit 100 ", preview);
        Assert.Contains("-cluster_time_limit 1000 ", recording);
        Assert.DoesNotContain("zerolatency", recording);
        Assert.DoesNotContain("-bf 0", recording);
        Assert.EndsWith("-f matroska \"pipe:1\"", preview);
        Assert.Equal(audio != AudioSourceType.None, preview.Contains("-c:a aac"));
        Assert.Equal(recording.Contains("-c:a aac"), preview.Contains("-c:a aac"));
        Assert.Equal(recording, provider.BuildOutputArguments(config, HardwareEncoderType.SoftwareCpu, "pipe:1"));
    }

    [Theory]
    [InlineData(false, HardwareEncoderType.NvidiaNvenc, "h264_nvenc")]
    [InlineData(false, HardwareEncoderType.IntelQsv, "h264_qsv")]
    [InlineData(false, HardwareEncoderType.AmdAmf, "h264_amf")]
    [InlineData(true, HardwareEncoderType.AppleVideoToolbox, "h264_videotoolbox")]
    public void HardwarePreviewKeepsSelectedEncoderAndDoesNotApplyX264OnlyOptions(bool mac, HardwareEncoderType encoder, string codec)
    {
        IFFmpegPlatformProvider provider = mac ? new MacOsFFmpegProvider() : new WindowsFFmpegProvider();
        var config = new RecordingConfiguration();
        var preview = provider.BuildLivePreviewOutputArguments(config, encoder, "pipe:1");
        Assert.Contains("-c:v " + codec, preview);
        Assert.Contains("-cluster_time_limit 100 ", preview);
        Assert.DoesNotContain("zerolatency", preview);
        Assert.Equal(provider.BuildOutputArguments(config, encoder, "pipe:1"),
            preview.Replace("-cluster_time_limit 100 ", "-cluster_time_limit 1000 ", StringComparison.Ordinal));
    }
}

public partial class FFmpegStartupHandshakeTests
{
    [UnixOnlyFact]
    public Task LivePreviewUsesItsExplicitProfile() => VerifyBoundOutputProfile(true);

    [UnixOnlyFact]
    public Task BoundRecordingStreamKeepsTheSavedRecordingProfile() => VerifyBoundOutputProfile(false);

    private async Task VerifyBoundOutputProfile(bool preview)
    {
        var expected = preview ? "live-preview" : "saved-recording";
        var executable = CreateExecutable(
            $"case \"$*\" in *'profile={expected}'*) ;; *) echo 'wrong output profile' >&2; exit 1;; esac\n" +
            "echo 'frame=1' >&2\nwhile IFS= read -r line; do [ \"$line\" = q ] && exit 0; done");
        try
        {
            using var output = new MemoryStream();
            await using var engine = new FFmpegScreenRecorderEngine(new ProfileProvider(), ffmpegPath: executable,
                pinnedEncoder: HardwareEncoderType.SoftwareCpu, livePreviewOutput: preview) { OutputStream = output };
            await engine.StartRecordingAsync("bound-output.mkv", Config(), new(0, 0, 128, 72));
            Assert.True(engine.IsRunning);
            await engine.StopRecordingAsync();
        }
        finally { File.Delete(executable); }
    }

    [Fact]
    public async Task LivePreviewCannotAccidentallyUseRecordingFileOutput()
    {
        await using var engine = new FFmpegScreenRecorderEngine(new ProfileProvider(),
            pinnedEncoder: HardwareEncoderType.SoftwareCpu, livePreviewOutput: true);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "must-not-exist.mkv");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync(path, Config(), new(0, 0, 128, 72)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    private sealed class ProfileProvider : IFFmpegPlatformProvider
    {
        public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes() => [];
        public string BuildInputArguments(RecordingConfiguration config, int x, int y, int width, int height,
            bool synthetic, bool hasDirectShowMic, string? systemAudioPipeArg = null, string? microphoneAudioPipeArg = null) => "";
        public string BuildOutputArguments(RecordingConfiguration config, HardwareEncoderType encoder, string path) => "-metadata profile=saved-recording -f null -";
        public string BuildLivePreviewOutputArguments(RecordingConfiguration config, HardwareEncoderType encoder, string path) => "-metadata profile=live-preview -f null -";
        public string BuildDirectPreviewArguments(RecordingConfiguration config, int x, int y, int width, int height,
            bool synthetic, bool hasDirectShowMic, string? systemAudioPipeArg, string? microphoneAudioPipeArg, string? videoInput = null) =>
            "-metadata profile=live-preview -f null -";
    }
}
