// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>
/// Immutable, shared timing recipe for preview and export. Ranges are half-open.
/// Frame/sample boundaries are ceilings of exact cumulative time, never sums of
/// independently rounded clip durations. A sub-frame clip may own zero output frames.
/// </summary>
public sealed class ProjectRenderPlan
{
    public const int AudioSampleRate = 48000;
    public Guid ProjectId { get; }
    public long Revision { get; }
    public ProjectCanvas Canvas { get; }
    public ImmutableArray<ProjectRenderClip> Clips { get; }
    public long DurationTicks { get; }
    public long FrameCount { get; }
    public long AudioSampleCount { get; }

    private ProjectRenderPlan(RecordingProject project, ImmutableArray<ProjectRenderClip> clips,
        long durationTicks, long frames, long samples)
    {
        ProjectId = project.ProjectId;
        Revision = project.Revision;
        Canvas = project.Canvas;
        Clips = clips;
        DurationTicks = durationTicks;
        FrameCount = frames;
        AudioSampleCount = samples;
    }

    public static ProjectRenderPlan Create(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        var sources = project.Sources.ToDictionary(s => s.Id);
        var clips = ImmutableArray.CreateBuilder<ProjectRenderClip>(project.Clips.Length);
        BigInteger numerator = 0, denominator = 1;
        long ticks = 0, frames = 0, samples = 0;
        foreach (var clip in project.Clips)
        {
            var source = sources[clip.SourceId];
            var timeBase = source.Timing.TimeBase;
            var start = new ProjectExactTime(numerator, denominator);
            var gcd = BigInteger.GreatestCommonDivisor(denominator, timeBase.Denominator);
            var multiplier = timeBase.Denominator / gcd;
            numerator = numerator * multiplier +
                ((BigInteger)clip.OutPts - clip.InPts) * timeBase.Numerator * (denominator / gcd);
            denominator *= multiplier;
            var reduction = BigInteger.GreatestCommonDivisor(numerator, denominator);
            numerator /= reduction;
            denominator /= reduction;
            if (denominator.GetBitLength() > 4096)
                throw new InvalidDataException("Render time-base complexity exceeds supported limits.");
            var end = new ProjectExactTime(numerator, denominator);
            var nextTicks = end.CeilingUnits(TimeSpan.TicksPerSecond);
            if (nextTicks <= ticks)
                throw new InvalidDataException("Clip is shorter than timeline precision.");
            var nextFrames = end.CeilingUnits(project.Canvas.Fps.Numerator, project.Canvas.Fps.Denominator);
            var nextSamples = end.CeilingUnits(AudioSampleRate);
            clips.Add(new(clip, source, start, end, ticks, nextTicks, frames, nextFrames, samples, nextSamples));
            (ticks, frames, samples) = (nextTicks, nextFrames, nextSamples);
        }
        return new(project, clips.MoveToImmutable(), ticks, frames, samples);
    }

    /// <summary>Maps an output frame to a source PTS without intermediate tick rounding.</summary>
    public ProjectPosition? LocateVideoFrame(long frame)
    {
        if (frame < 0 || frame >= FrameCount) return null;
        var low = 0;
        var high = Clips.Length - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var segment = Clips[mid];
            if (frame < segment.StartFrame) high = mid - 1;
            else if (frame >= segment.EndFrame) low = mid + 1;
            else
            {
                var start = segment.ExactStart;
                var timeBase = segment.Source.Timing.TimeBase;
                var n = (BigInteger)frame * Canvas.Fps.Denominator * start.Denominator -
                    start.Numerator * Canvas.Fps.Numerator;
                var d = (BigInteger)Canvas.Fps.Numerator * start.Denominator;
                var delta = n * timeBase.Denominator / (d * timeBase.Numerator);
                var pts = BigInteger.Min(segment.Clip.OutPts - 1,
                    BigInteger.Max(segment.Clip.InPts, (BigInteger)segment.Clip.InPts + delta));
                return new(segment.Clip.Id, segment.Source.Id, (long)pts);
            }
        }
        throw new InvalidDataException("Output frame has no owning clip.");
    }
}

/// <summary>Exact seconds, retained so media consumers do not round-trip through UI ticks.</summary>
public sealed record ProjectExactTime(BigInteger Numerator, BigInteger Denominator)
{
    internal long CeilingUnits(long units, long divisor = 1)
    {
        var n = Numerator * units;
        var d = Denominator * divisor;
        var quotient = BigInteger.DivRem(n, d, out var remainder);
        if (remainder > 0) quotient++;
        if (quotient < long.MinValue || quotient > long.MaxValue)
            throw new InvalidDataException("Render duration exceeds supported limits.");
        return (long)quotient;
    }
}

public sealed record ProjectRenderClip(
    ProjectClip Clip, ProjectSource Source,
    ProjectExactTime ExactStart, ProjectExactTime ExactEnd,
    long StartTicks, long EndTicks,
    long StartFrame, long EndFrame,
    long StartAudioSample, long EndAudioSample)
{
    /// <summary>
    /// Position of an audio PTS relative to the first owned output sample. Positive means
    /// leading silence; negative means pre-roll to discard. Do not reset audio and
    /// video independently to STARTPTS, which would erase their recorded offset.
    /// </summary>
    public long AudioSampleOffset(long audioPts, ProjectRational audioTimeBase)
    {
        if (audioTimeBase is null || audioTimeBase.Numerator is <= 0 or > int.MaxValue ||
            audioTimeBase.Denominator is <= 0 or > int.MaxValue)
            throw new InvalidDataException("Invalid audio time base.");
        var videoBase = Source.Timing.TimeBase;
        var n = (BigInteger)audioPts * audioTimeBase.Numerator * videoBase.Denominator -
            (BigInteger)Clip.InPts * videoBase.Numerator * audioTimeBase.Denominator;
        var d = (BigInteger)audioTimeBase.Denominator * videoBase.Denominator;
        var global = new ProjectExactTime(
            ExactStart.Numerator * d + n * ExactStart.Denominator,
            ExactStart.Denominator * d);
        return checked(global.CeilingUnits(ProjectRenderPlan.AudioSampleRate) - StartAudioSample);
    }
}
