// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectWaveformReaderTests
{
    [MacOsOnlyFact]
    public async Task ActualSourcePulse_ProducesWaveformWithoutLoadingWholeSource()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-waveform-reader-");
        try
        {
            var path = Path.Combine(directory.FullName, "source.mkv");
            await ProjectPcmExtractionTests.Generate(path, true);
            await using var input = File.OpenRead(path);
            var reader = new ProjectWaveformReader(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            var result = await reader.ReadAsync(input, 2000, 3000, new(1, 1000), true, 100, default);
            Assert.Equal(48000, result.SampleCount);
            Assert.Equal(100, result.Buckets.Length);
            Assert.InRange(result.Buckets[25].Rms, .24f, .26f);
            Assert.Equal(0, result.Buckets[10].Rms);
            Assert.Equal(0, result.Buckets[70].Rms);
            Assert.True(input.CanRead);
        }
        finally { directory.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task SilentLongInterval_UsesBoundedChunksAndExactTotalSamples()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-waveform-chunks-");
        try
        {
            var path = Path.Combine(directory.FullName, "source.mkv");
            await ProjectPcmExtractionTests.Generate(path, false, 61);
            await using var input = File.OpenRead(path);
            var reader = new ProjectWaveformReader(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            // 1,801 / 30 seconds: longer than the 30-second decoder quota, fractional final chunk.
            var result = await reader.ReadAsync(input, 60, 1861, new(1, 30), false, 128, default);
            Assert.Equal(2881600, result.SampleCount);
            Assert.False(result.HasAudio);
            Assert.True(result.IsSilent);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task PreCancelled_DoesNotStartDecode()
    {
        var reader = new ProjectWaveformReader(new NeverStart());
        var path = Path.GetTempFileName();
        try
        {
            await using var input = File.OpenRead(path);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                reader.ReadAsync(input, 0, 1000, new(1, 1000), true, 64, new(true)));
        }
        finally { File.Delete(path); }
    }

    private sealed class NeverStart : IProjectMediaProcess
    {
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct) => throw new InvalidOperationException("Canceled work reached decoder.");
    }
}
