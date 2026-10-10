// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

namespace ScreenRecorder.Media.Projects;

public sealed class ProjectRemuxIncompatibleException(string message) : IOException(message);

public static class ProjectRemuxVerifier
{
    public static async Task VerifyAsync(IProjectMediaProcess process, IProjectStreamingMediaProcess streaming,
        FileStream output, IReadOnlyList<ProjectSourceEvidence> expected, bool audioConverted, CancellationToken ct,
        long? normalizedAudioSamples = null)
    {
        if (expected.Count is < 1 or > 128) throw new ArgumentException("Expected source count exceeds limit.");
        if (normalizedAudioSamples is <= 0 || (normalizedAudioSamples is not null && !audioConverted))
            throw new ArgumentException("Normalized audio requires conversion and a positive sample count.");
        using var packetHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var verifiedHashes = new List<string>();
        var videoRanges = expected.Select(e => new PacketRange(e.Video.PacketCount)).ToArray();
        var audioRanges = expected.Where(e => e.Audio is not null).Select(e => new PacketRange(e.Audio!.PacketCount)).ToArray();
        var indices = new int[2];
        var actual = await ProjectPacketInspector.InspectAsync(streaming, output, ct, (index, pts, duration) =>
        {
            if (index is < 0 or > 1 || (index == 1 && audioConverted)) return;
            var ranges = index == 0 ? videoRanges : audioRanges;
            if (indices[index] >= ranges.Length) throw new ProjectRemuxIncompatibleException("Unexpected extra packets.");
            var range = ranges[indices[index]];
            range.Add(pts, duration);
            if (range.Count == range.Expected)
            {
                if (index == 0 && normalizedAudioSamples is not null)
                    verifiedHashes.Add(Convert.ToHexString(packetHash.GetHashAndReset()));
                indices[index]++;
            }
        }, (index, hash) =>
        {
            if (index != 0 || normalizedAudioSamples is null) return;
            if (hash.Length != 71 || !hash.StartsWith("SHA256:", StringComparison.Ordinal) || !hash.AsSpan(7).ToArray().All(Uri.IsHexDigit))
                throw new ProjectRemuxIncompatibleException("Missing video packet hash.");
            packetHash.AppendData(Convert.FromHexString(hash[7..]));
        });
        if (normalizedAudioSamples is not null && (verifiedHashes.Count != expected.Count ||
            expected.Where((e, i) => !string.Equals(e.Video.PacketHash, verifiedHashes[i], StringComparison.OrdinalIgnoreCase)).Any()))
            throw new ProjectRemuxIncompatibleException("Copied video payload differs from source packets.");
        var source = expected[0];
        var video = source.Video; var result = actual.Video;
        if (normalizedAudioSamples is not null)
        {
            // Probe time bases are emitted after packets. A second bounded streaming pass
            // checks every presentation offset at the original source's tick precision.
            using var timingHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var segment = 0;
            long packets = 0, origin = 0;
            await ProjectPacketInspector.InspectAsync(streaming, output, ct, (index, pts, duration) =>
            {
                if (index != 0) return;
                if (segment >= expected.Count) throw new ProjectRemuxIncompatibleException("Extra video timing packets.");
                var reference = expected[segment].Video;
                if (packets == 0) origin = pts;
                var relative = checked(pts - origin) * result.TickSeconds / reference.TickSeconds;
                var tick = checked((long)Math.Round(relative));
                Span<byte> bytes = stackalloc byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes, tick);
                timingHash.AppendData(bytes);
                if (++packets != reference.PacketCount) return;
                if (!string.Equals(Convert.ToHexString(timingHash.GetHashAndReset()), reference.PresentationTimingHash,
                    StringComparison.OrdinalIgnoreCase))
                    throw new ProjectRemuxIncompatibleException("Copied video presentation timing differs from source.");
                segment++;
                packets = 0;
            });
            if (segment != expected.Count || packets != 0)
                throw new ProjectRemuxIncompatibleException("Incomplete video timing evidence.");
        }
        var tolerance = 2 * (video.TickSeconds + result.TickSeconds);
        if (result.Codec != video.Codec || result.Width != video.Width || result.Height != video.Height ||
            result.PixelFormat != video.PixelFormat || result.PacketCount != expected.Sum(s => s.Video.PacketCount) || !result.MonotonicDts ||
            !result.StartsWithKeyframe || Math.Abs(result.StartSeconds) > tolerance ||
            Math.Abs(result.EndSeconds - expected.Sum(s => s.Video.EndSeconds - s.Video.StartSeconds)) > tolerance)
            throw new ProjectRemuxIncompatibleException("Remux video packets or timing do not match the recording.");
        if ((source.Audio is null) != (actual.Audio is null))
            throw new ProjectRemuxIncompatibleException("Remux audio stream is missing or unexpected.");
        if (source.Audio is { } audio && actual.Audio is { } resultAudio)
        {
            var audioTolerance = 2 * (audio.TickSeconds + resultAudio.TickSeconds) +
                1024d / resultAudio.SampleRate;
            var normalized = normalizedAudioSamples is not null;
            if (resultAudio.Codec != "aac" || resultAudio.SampleRate != (normalized ? 48000 : audio.SampleRate) ||
                resultAudio.ChannelLayout != (normalized ? "stereo" : audio.ChannelLayout) || !resultAudio.MonotonicDts ||
                (!audioConverted && resultAudio.PacketCount != expected.Sum(s => s.Audio?.PacketCount ?? 0)) ||
                Math.Abs(resultAudio.StartSeconds - (normalized ? 0 : audio.StartSeconds - video.StartSeconds)) > audioTolerance ||
                Math.Abs(resultAudio.EndSeconds - (normalized ? normalizedAudioSamples!.Value / 48000d :
                    expected.Take(expected.Count - 1).Sum(s => s.Video.EndSeconds - s.Video.StartSeconds) +
                    expected[^1].Audio!.EndSeconds - expected[^1].Video.StartSeconds)) > audioTolerance)
                throw new ProjectRemuxIncompatibleException($"Remux audio mismatch: source {audio}; output {resultAudio}; origin {video.StartSeconds}.");
        }
        double offset = 0;
        var checkpoints = new List<long>();
        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            CheckRange(videoRanges[i], result, offset, offset + e.Video.EndSeconds - e.Video.StartSeconds,
                2 * (e.Video.TickSeconds + result.TickSeconds));
            checkpoints.Add(videoRanges[i].First);
            checkpoints.Add(videoRanges[i].LastPts);
            if (!audioConverted && e.Audio is { } a && actual.Audio is { } ra)
                CheckRange(audioRanges[i], ra, offset + a.StartSeconds - e.Video.StartSeconds,
                    offset + a.EndSeconds - e.Video.StartSeconds, 2 * (a.TickSeconds + ra.TickSeconds) + 1024d / ra.SampleRate);
            offset += e.Video.EndSeconds - e.Video.StartSeconds;
        }
        // Bounded decode checkpoints at both sides of every join.
        foreach (var pts in checkpoints.Distinct())
            await process.RunAsync(ProjectMediaJob.ExtractFrame(pts, result.TimeBase, 64, 36), [output], null, ct);
    }

    private static void CheckRange(PacketRange range, ProjectStreamEvidence stream, double start, double end, double tolerance)
    {
        if (range.Count != range.Expected || Math.Abs(range.First * stream.TickSeconds - start) > tolerance ||
            Math.Abs(range.End * stream.TickSeconds - end) > tolerance)
            throw new ProjectRemuxIncompatibleException("Segment boundary differs from recorded timing.");
    }

    private sealed class PacketRange(long expected)
    {
        public long Expected = expected, Count, First = long.MaxValue, End = long.MinValue, LastPts = long.MinValue;
        public void Add(long pts, long duration)
        { Count++; First = Math.Min(First, pts); LastPts = Math.Max(LastPts, pts); End = Math.Max(End, checked(pts + duration)); }
    }
}
