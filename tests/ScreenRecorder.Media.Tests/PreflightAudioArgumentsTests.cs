using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Media.FFmpeg;
using System.Diagnostics;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioArgumentsTests
{
    private static IPreflightAudioPlatformProvider Provider(bool mac) =>
        mac ? new MacOsFFmpegProvider() : new WindowsFFmpegProvider();

    [Theory]
    [InlineData(false, AudioSourceType.MicrophoneOnly, false, true)]
    [InlineData(false, AudioSourceType.SystemOnly, true, false)]
    [InlineData(false, AudioSourceType.SystemAndMicrophone, true, true)]
    [InlineData(true, AudioSourceType.MicrophoneOnly, false, true)]
    [InlineData(true, AudioSourceType.SystemOnly, true, false)]
    [InlineData(true, AudioSourceType.SystemAndMicrophone, true, true)]
    public void SelectedSourcesOnly(bool mac, AudioSourceType audio, bool system, bool mic)
    {
        var args = Provider(mac).BuildPreflightAudioInputArguments(new() { AudioSource = audio }, false,
            "-f s16le -ar 48000 -ac 2 -i system-pipe", "-f s16le -ar 44100 -ac 1 -i microphone-pipe");
        Assert.Equal(system, args.Contains("system-pipe"));
        Assert.Equal(mic, args.Contains("microphone-pipe"));
        Assert.DoesNotContain("anullsrc", args);
        Assert.Contains(system && mic ? "amix=inputs=2" : "-map 0:a", args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoVideoInputOrMapping(bool mac)
    {
        foreach (var audio in new[] { AudioSourceType.SystemOnly, AudioSourceType.MicrophoneOnly, AudioSourceType.SystemAndMicrophone })
        {
            var args = Provider(mac).BuildPreflightAudioInputArguments(new() { AudioSource = audio }, true, null, null);
            Assert.DoesNotContain("testsrc", args);
            Assert.DoesNotContain("desktop", args);
            Assert.DoesNotContain("0:v", args);
            Assert.DoesNotContain("gdigrab", args);
            Assert.DoesNotContain("avfoundation", args);
        }
        Assert.Throws<InvalidOperationException>(() => Provider(mac).BuildPreflightAudioInputArguments(
            new() { AudioSource = AudioSourceType.None, MaintainSegmentAudioTrack = true }, true, null, null));
        Assert.Throws<InvalidOperationException>(() => Provider(mac).BuildPreflightAudioInputArguments(
            new() { AudioSource = AudioSourceType.SystemOnly }, false, null, null));
    }

    [Fact]
    public void WindowsMicrophoneKeepsSelectedDirectShowDevice()
    {
        var args = Provider(false).BuildPreflightAudioInputArguments(
            new() { AudioSource = AudioSourceType.MicrophoneOnly, MicrophoneDeviceId = "Selected microphone" },
            false, null, null);
        Assert.Contains("-f dshow -i audio=\"Selected microphone\"", args);
        Assert.Contains("-map 0:a", args);
        Assert.DoesNotContain("desktop", args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrialOutputIsPcmAudioWithoutVideo(bool trial)
    {
        var args = PreflightAudioFormat.OutputArguments(trial);
        Assert.Contains("-vn", args);
        Assert.Contains("-ar 48000 -ac 2", args);
        Assert.Contains("-c:a pcm_s16le", args);
        Assert.Contains(trial ? "-f matroska pipe:1" : "-f null -", args);
        Assert.DoesNotContain("-c:v", args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedFormatsKeepBothSources(bool mac)
    {
        // The silent 48k stereo source must not silence the 44.1k mono microphone.
        var input = Provider(mac).BuildPreflightAudioInputArguments(
            new() { AudioSource = AudioSourceType.SystemAndMicrophone }, false,
            "-f lavfi -i anullsrc=r=48000:cl=stereo",
            "-f lavfi -i sine=frequency=440:sample_rate=44100");
        using var process = new Process { StartInfo = new(FFmpegDiscovery.FindFFmpegExecutable()!)
        {
            Arguments = $"-v error {input} -t 0.2 -vn -ar 48000 -ac 2 -f f32le pipe:1",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pcm = new MemoryStream();
        process.Start();
        try
        {
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(pcm, deadline.Token),
                process.WaitForExitAsync(deadline.Token));
            Assert.Equal("", await errors);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(9600 * 2 * sizeof(float), pcm.Length);
            var data = pcm.ToArray();
            var samples = Enumerable.Range(0, data.Length / sizeof(float))
                .Select(i => BitConverter.ToSingle(data, i * sizeof(float))).ToArray();
            Assert.All(samples, value => Assert.True(float.IsFinite(value)));
            Assert.InRange(Math.Sqrt(samples.Average(value => (double)value * value)), 0.01, 0.1);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
        }
    }
}
