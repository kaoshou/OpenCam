// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectPosition(Guid ClipId, Guid SourceId, long SourcePts);
public sealed record ProjectTimelineClip(Guid ClipId, long StartTicks, long EndTicks);

public sealed class ProjectTimeline
{
    internal sealed record Segment(ProjectClip Clip, ProjectRational TimeBase, long StartTicks, long EndTicks,
        BigInteger ExactStartNumerator, BigInteger ExactStartDenominator);
    internal IReadOnlyList<Segment> Segments { get; }
    public long DurationTicks { get; }
    public IReadOnlyList<ProjectTimelineClip> Clips { get; }

    private ProjectTimeline(List<Segment> segments, long duration)
    {
        Segments = segments.AsReadOnly();
        DurationTicks = duration;
        Clips = segments.Select(s => new ProjectTimelineClip(s.Clip.Id, s.StartTicks, s.EndTicks)).ToList().AsReadOnly();
    }

    public static ProjectTimeline Build(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        var sources = project.Sources.ToDictionary(s => s.Id);
        var segments = new List<Segment>(project.Clips.Length);
        BigInteger numerator = 0, denominator = 1;
        long start = 0;
        foreach (var clip in project.Clips)
        {
            var exactStartNumerator = numerator;
            var exactStartDenominator = denominator;
            var timeBase = sources[clip.SourceId].Timing.TimeBase;
            var durationNumerator = ((BigInteger)clip.OutPts - clip.InPts) * timeBase.Numerator * TimeSpan.TicksPerSecond;
            var gcd = BigInteger.GreatestCommonDivisor(denominator, timeBase.Denominator);
            var multiplier = timeBase.Denominator / gcd;
            numerator = numerator * multiplier + durationNumerator * (denominator / gcd);
            denominator *= multiplier;
            var reduction = BigInteger.GreatestCommonDivisor(numerator, denominator);
            numerator /= reduction;
            denominator /= reduction;
            // Bound adversarial combinations of thousands of coprime media time bases.
            var roundedEnd = (numerator + denominator - 1) / denominator;
            if (denominator.GetBitLength() > 4096 || roundedEnd > long.MaxValue)
                throw new InvalidDataException("Timeline duration or time-base complexity exceeds supported limits.");
            // First representable tick at or after the exact boundary. Split must not move a frame early.
            var end = (long)roundedEnd;
            if (end <= start) throw new InvalidDataException("Clip is shorter than timeline precision.");
            segments.Add(new(clip, timeBase, start, end, exactStartNumerator, exactStartDenominator));
            start = end;
        }
        return new(segments, start);
    }

    public ProjectPosition? Locate(long timelineTicks)
    {
        if (timelineTicks < 0 || timelineTicks >= DurationTicks) return null;
        var low = 0;
        var high = Segments.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var segment = Segments[mid];
            if (timelineTicks < segment.StartTicks) high = mid - 1;
            else if (timelineTicks >= segment.EndTicks) low = mid + 1;
            else return new(segment.Clip.Id, segment.Clip.SourceId, SourcePts(segment, timelineTicks));
        }
        return null;
    }

    internal static long SourcePts(Segment segment, long ticks)
    {
        if (ticks <= segment.StartTicks) return segment.Clip.InPts;
        if (ticks >= segment.EndTicks) return segment.Clip.OutPts;
        var exactOffset = (BigInteger)ticks * segment.ExactStartDenominator - segment.ExactStartNumerator;
        var delta = exactOffset * segment.TimeBase.Denominator /
            (segment.ExactStartDenominator * TimeSpan.TicksPerSecond * segment.TimeBase.Numerator);
        return (long)BigInteger.Min((BigInteger)segment.Clip.InPts + delta, (BigInteger)segment.Clip.OutPts - 1);
    }
}
