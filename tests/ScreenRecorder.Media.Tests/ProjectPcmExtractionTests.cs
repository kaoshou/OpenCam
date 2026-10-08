// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Diagnostics;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPcmExtractionTests
{
    [MacOsOnlyFact]
    public async Task LateAudio_RetainsLeadingSilenceAndExactInterval()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pcm-test-");
        try
        {
            var path = Path.Combine(root.FullName, "late.mkv");
            await Generate(path, true);
            await using var input = File.OpenRead(path);
            var result = await new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                ProjectMediaJob.ExtractPcm(2000, 3000, new(1, 1000), hasAudio: true), [input], null, default);
            var samples = Samples(result.Output);
            Assert.Equal(48000, samples.Length);
            Assert.All(samples[..9600], sample => Assert.Equal(0, sample));
            Assert.InRange(samples[10000], .24f, .26f);
            Assert.All(samples[20000..], sample => Assert.Equal(0, sample));
            var waveform = new ProjectWaveformBuilder(48000, 100, true);
            waveform.Append(result.Output);
            var peaks = waveform.Complete();
            Assert.Equal(0, peaks.Buckets[10].Rms);
            Assert.InRange(peaks.Buckets[25].Rms, .24f, .26f);
            Assert.Equal(0, peaks.Buckets[60].Rms);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task TrimInsideAudio_DoesNotRetainRemovedLeadingSilence()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pcm-trim-");
        try
        {
            var path = Path.Combine(root.FullName, "late.mkv");
            await Generate(path, true);
            await using var input = File.OpenRead(path);
            var result = await new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                ProjectMediaJob.ExtractPcm(2250, 2500, new(1, 1000), hasAudio: true), [input], null, default);
            var samples = Samples(result.Output);
            Assert.Equal(12000, samples.Length);
            Assert.InRange(samples[100], .24f, .26f);
            Assert.All(samples[8000..], sample => Assert.Equal(0, sample));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task SourceWithoutAudio_ProducesTimedSilence()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pcm-silent-");
        try
        {
            var path = Path.Combine(root.FullName, "silent.mkv");
            await Generate(path, false);
            await using var input = File.OpenRead(path);
            var result = await new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                ProjectMediaJob.ExtractPcm(2000, 2500, new(1, 1000), hasAudio: false), [input], null, default);
            Assert.Equal(96000, result.Output.Length);
            Assert.All(result.Output, value => Assert.Equal(0, value));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void UnboundedPcmRequest_IsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectMediaJob.ExtractPcm(0, 31000, new(1, 1000), true));

    [MacOsOnlyFact]
    public async Task TrimAfterAudioEnds_IsStillTimedSilence()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pcm-tail-");
        try
        {
            var path = Path.Combine(root.FullName, "late.mkv");
            await Generate(path, true);
            await using var input = File.OpenRead(path);
            var result = await new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                ProjectMediaJob.ExtractPcm(3000, 3500, new(1, 1000), true), [input], null, default);
            Assert.Equal(96000, result.Output.Length);
            Assert.All(result.Output, value => Assert.Equal(0, value));
        }
        finally { root.Delete(true); }
    }

    private static float[] Samples(byte[] bytes) => Enumerable.Range(0, bytes.Length / 4)
        .Select(i => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4)))).ToArray();

    internal static async Task Generate(string path, bool audio, int videoDuration = 2)
    {
        var start = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!) {
            UseShellExecute = false, RedirectStandardError = true };
        string[] args = ["-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
            $"color=c=blue:s=64x36:r=30:d={videoDuration}",
            ..(audio ? new[] { "-f", "lavfi", "-i", "aevalsrc=0.25:s=48000:d=0.2",
                "-filter_complex", "[0:v]setpts=PTS+2/TB[v];[1:a]asetpts=PTS+2.2/TB[a]",
                "-map", "[v]", "-map", "[a]", "-c:a", "pcm_s16le" }
                : new[] { "-vf", "setpts=PTS+2/TB", "-an" }),
            "-c:v", "libx264", "-bf", "0", path];
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancel = timeout.Token.Register(() => { try { child.Kill(); } catch (InvalidOperationException) { } });
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, child.ExitCode);
        Assert.Empty(await errors);
    }
}
