// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

public enum ProjectExportStrategy { CopySingle, ConvertAudioOnly, ConcatCopy, ConcatConvertAudio, Render }
public sealed record ProjectExportDecision(ProjectExportStrategy Strategy, string Reason);
public sealed record ProjectStreamEvidence(string Codec, string InitializationHash, int Width, int Height,
    string PixelFormat, ProjectRational TimeBase, string FrameRate, int SampleRate, string ChannelLayout,
    long PacketCount, long FirstPts, long EndPts, long MaximumDuration, bool StartsWithKeyframe, bool MonotonicDts)
{
    public double StartSeconds => (double)FirstPts * TimeBase.Numerator / TimeBase.Denominator;
    public double EndSeconds => (double)EndPts * TimeBase.Numerator / TimeBase.Denominator;
    public double TickSeconds => (double)TimeBase.Numerator / TimeBase.Denominator;
    public bool CompatibleWith(ProjectStreamEvidence other) =>
        Codec == other.Codec && InitializationHash.Length > 0 && InitializationHash == other.InitializationHash &&
        Width == other.Width && Height == other.Height && PixelFormat == other.PixelFormat &&
        TimeBase == other.TimeBase && FrameRate == other.FrameRate && SampleRate == other.SampleRate &&
        ChannelLayout == other.ChannelLayout;
}
public sealed record ProjectSourceEvidence(ProjectStreamEvidence Video, ProjectStreamEvidence? Audio)
{
    public double FormatStartSeconds { get; init; }
}

public static class ProjectExportPlanner
{
    public static bool HasOnlyWholeClips(ProjectRenderPlan plan) => !plan.Clips.IsEmpty && plan.Clips.All(part =>
        part.Clip.InPts == part.Source.Timing.StartPts && part.Clip.OutPts == part.Source.Timing.StartPts + part.Source.Timing.DurationTs &&
        part.Clip.Volume == 1 && !part.Clip.Muted && part.Clip.FadeInTicks == 0 && part.Clip.FadeOutTicks == 0 &&
        part.Clip.Crop == 0 && part.Clip.Scale == 1 && part.Clip.PositionX == 0 && part.Clip.PositionY == 0);
    public static ProjectExportDecision Select(ProjectRenderPlan plan, IReadOnlyList<ProjectSourceEvidence> sources)
    {
        ProjectExportDecision Render(string reason) => new(ProjectExportStrategy.Render, reason);
        if (plan.Clips.IsEmpty || sources.Count != plan.Clips.Length) return Render("Missing source evidence");
        for (var i = 0; i < plan.Clips.Length; i++)
        {
            var part = plan.Clips[i]; var clip = part.Clip; var source = part.Source; var video = sources[i].Video;
            if (clip.InPts != source.Timing.StartPts || clip.OutPts != source.Timing.StartPts + source.Timing.DurationTs ||
                clip.Volume != 1 || clip.Muted || clip.FadeInTicks != 0 || clip.FadeOutTicks != 0 || clip.Crop != 0 ||
                clip.Scale != 1 || clip.PositionX != 0 || clip.PositionY != 0)
                return Render("Timeline contains trims or effects");
            if (video.Codec != "h264" || video.Width != plan.Canvas.Width || video.Height != plan.Canvas.Height ||
                !video.StartsWithKeyframe || !video.MonotonicDts || video.PacketCount <= 0 || video.EndPts <= video.FirstPts ||
                video.InitializationHash.Length == 0 || sources[i].Audio is { MonotonicDts: false })
                return Render("Source is not compatible with direct MP4 output");
            var tick = (double)source.Timing.TimeBase.Numerator / source.Timing.TimeBase.Denominator;
            if (Math.Abs(clip.InPts * tick - video.StartSeconds) > 2 * (tick + video.TickSeconds) ||
                Math.Abs(clip.OutPts * tick - video.EndSeconds) > 2 * (tick + video.TickSeconds))
                return Render("Saved range differs from actual source timing");
        }
        if (sources.Count == 1)
            return new(sources[0].Audio is { Codec: not "aac" } ? ProjectExportStrategy.ConvertAudioOnly : ProjectExportStrategy.CopySingle,
                "Whole recording; preserve encoded video");
        if (sources.Count > 128) return Render("Too many simultaneous concat handles");
        var first = sources[0];
        foreach (var source in sources)
            if (!first.Video.CompatibleWith(source.Video) || (first.Audio is null) != (source.Audio is null) ||
                Math.Abs((first.Video.StartSeconds - first.FormatStartSeconds) -
                    (source.Video.StartSeconds - source.FormatStartSeconds)) > .000001 ||
                (first.Audio is not null && (first.Audio.Codec != "aac" || !first.Audio.CompatibleWith(source.Audio!))))
                return Render("Segment stream parameters differ");
        if (first.Audio is not null && sources.Any(s => s.Audio!.StartSeconds != s.Video.StartSeconds ||
            Math.Abs(s.Audio.PacketCount * 1024d / s.Audio.SampleRate - (s.Video.EndSeconds - s.Video.StartSeconds)) > 1d / s.Audio.SampleRate))
            return new(ProjectExportStrategy.ConcatConvertAudio, "Preserve video; normalize AAC priming/padding once");
        return new(ProjectExportStrategy.ConcatCopy, "Compatible complete segments");
    }
}
