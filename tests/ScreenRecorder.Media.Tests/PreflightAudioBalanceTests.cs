using System.Diagnostics;
using System.Text.Json;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioBalanceTests
{
    [Theory]
    [InlineData(false, 100, 25, .225)]
    [InlineData(true, 100, 25, .225)]
    [InlineData(false, 0, 100, .1)]
    [InlineData(true, 0, 0, 0)]
    [InlineData(false, 100, 100, .3)]
    public async Task MixUsesIndependentAttenuationWithoutAutomaticAveraging(bool mac, int system, int mic, double expected)
    {
        var config = JsonSerializer.Deserialize<RecordingConfiguration>($"{{\"SystemAudioVolumePercent\":{system},\"MicrophoneVolumePercent\":{mic}}}")!;
        var input = PreflightAudioFormat.BuildInput(config, false,
            "-f lavfi -i aevalsrc=0.2|0.2:s=48000:c=stereo", "-f lavfi -i aevalsrc=0.1|0.1:s=48000:c=stereo", mac);
        using var process = new Process { StartInfo = new(FFmpegDiscovery.FindFFmpegExecutable()!)
        {
            Arguments = $"-v error {input} -t 0.1 -ar 48000 -ac 2 -c:a pcm_f32le -f f32le pipe:1",
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        }};
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        process.Start();
        using var output = new MemoryStream();
        try
        {
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token), process.WaitForExitAsync(timeout.Token));
            Assert.Equal(0, process.ExitCode); Assert.Equal("", await errors);
            Assert.Equal(38400, output.Length);
            var data = output.ToArray();
            for (int i = 0; i < data.Length; i += 4)
                Assert.InRange(BitConverter.ToSingle(data, i), expected - .001, expected + .001);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task SyntheticCapturePublishesActualMixAndZeroGainRemainsHealthy()
    {
        var config = new RecordingConfiguration { AudioSource = AudioSourceType.SystemAndMicrophone,
            SystemAudioVolumePercent = 0, MicrophoneVolumePercent = 0 };
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), synthetic: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await capture.StartAsync(config, null, timeout.Token);
        try
        {
            AudioLevelSample? result;
            do { await Task.Delay(100, timeout.Token); result = capture.ReadMix(DateTimeOffset.UtcNow); } while (result is null);
            var sample = result.Value;
            Assert.Equal(0, sample.Peak); Assert.True(capture.IsRunning); Assert.Null(capture.ErrorCode);
            Assert.NotEqual(ScreenRecorder.Core.Preflight.PreflightSourceState.Unavailable, capture.ReadMicrophone(DateTimeOffset.UtcNow).State);
        }
        finally { await capture.StopAsync(timeout.Token); }
    }
}
