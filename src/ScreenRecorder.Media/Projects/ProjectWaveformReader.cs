// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Waveform-only reduction; bounded PCM chunks, no complete-source PCM allocation.</summary>
public sealed class ProjectWaveformReader(IProjectMediaProcess process)
{
    public async Task<ProjectWaveformResult> ReadAsync(FileStream source, long startPts, long endPts,
        ProjectRational timeBase, bool hasAudio, int buckets, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (timeBase is null || timeBase.Numerator is <= 0 or > int.MaxValue ||
            timeBase.Denominator is <= 0 or > int.MaxValue || endPts <= startPts)
            throw new ArgumentOutOfRangeException(nameof(timeBase));
        var maximumChunkPts = (long)((BigInteger)30 * timeBase.Denominator / timeBase.Numerator);
        if (maximumChunkPts < 1) throw new InvalidDataException("Source time base exceeds waveform chunk precision.");
        var totalSamples = SamplesTo(endPts);
        var builder = new ProjectWaveformBuilder(totalSamples, buckets, hasAudio);
        long cursor = startPts, emitted = 0;
        while (cursor < endPts)
        {
            ct.ThrowIfCancellationRequested();
            var next = (long)BigInteger.Min(endPts, (BigInteger)cursor + maximumChunkPts);
            var job = ProjectMediaJob.ExtractPcm(cursor, next, timeBase, hasAudio);
            var decoded = await process.RunAsync(job, [source], null, ct);
            ct.ThrowIfCancellationRequested();
            var through = SamplesTo(next);
            var count = checked((int)(through - emitted));
            if (decoded.Output.Length != job.ExpectedOutputBytes || count > job.AudioSampleCount)
                throw new InvalidDataException("Waveform decoder returned an invalid chunk.");
            // Bucket positions use global quantization. A chunk's fractional trailing sample
            // must not add a sample on every boundary. This reduction is not an export audio stream.
            builder.Append(decoded.Output.AsSpan(0, checked(count * 4)));
            emitted = through;
            cursor = next;
        }
        return builder.Complete();

        long SamplesTo(long pts)
        {
            var n = ((BigInteger)pts - startPts) * timeBase.Numerator * 48000;
            var result = (n + timeBase.Denominator - 1) / timeBase.Denominator;
            if (result > long.MaxValue) throw new InvalidDataException("Waveform interval exceeds supported limits.");
            return (long)result;
        }
    }
}
