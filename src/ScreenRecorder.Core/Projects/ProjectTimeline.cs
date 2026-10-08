// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectPosition(Guid ClipId, Guid SourceId, long SourcePts);

public sealed class ProjectTimeline
{
    internal sealed record Segment(ProjectClip Clip, ProjectRational TimeBase, long StartTicks, long EndTicks);
    internal IReadOnlyList<Segment> Segments { get; }
    public long DurationTicks { get; }

    private ProjectTimeline(List<Segment> segments, long duration)
        => (Segments, DurationTicks) = (segments.AsReadOnly(), duration);

    public static ProjectTimeline Build(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        var sources = project.Sources.ToDictionary(s => s.Id);
        var segments = new List<Segment>(project.Clips.Length);
        BigInteger numerator = 0, denominator = 1;
        long start = 0;
        foreach (var clip in project.Clips)
        {
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
            if (denominator.GetBitLength() > 4096 || numerator / denominator > long.MaxValue)
                throw new InvalidDataException("Timeline duration or time-base complexity exceeds supported limits.");
            var end = (long)(numerator / denominator);
            if (end <= start) throw new InvalidDataException("Clip is shorter than timeline precision.");
            segments.Add(new(clip, timeBase, start, end));
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
        var delta = (BigInteger)(ticks - segment.StartTicks) * segment.TimeBase.Denominator /
            ((BigInteger)TimeSpan.TicksPerSecond * segment.TimeBase.Numerator);
        return (long)BigInteger.Min((BigInteger)segment.Clip.InPts + delta, (BigInteger)segment.Clip.OutPts - 1);
    }
}
