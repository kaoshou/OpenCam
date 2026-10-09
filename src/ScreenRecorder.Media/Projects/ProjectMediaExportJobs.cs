// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Seconds(long pts, ProjectRational timeBase) => Number((decimal)pts * timeBase.Numerator / timeBase.Denominator);

    public static ProjectMediaJob? CopyWholeRecording(ProjectRenderPlan plan)
    {
        if (plan.Clips.Length != 1) return null;
        var part = plan.Clips[0];
        var clip = part.Clip;
        var source = part.Source;
        if (clip.InPts != source.Timing.StartPts || clip.OutPts != source.Timing.StartPts + source.Timing.DurationTs ||
            clip.Volume != 1 || clip.Muted || clip.FadeInTicks != 0 || clip.FadeOutTicks != 0 || clip.Crop != 0 ||
            clip.Scale != 1 || clip.PositionX != 0 || clip.PositionY != 0 || source.VideoCodec != "h264" || source.AudioCodec != "aac" ||
            source.Width != plan.Canvas.Width || source.Height != plan.Canvas.Height) return null;
        ValidateTimestamp(clip.InPts, source.Timing.TimeBase);
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0)
        { RequiresOutput = true, IsLongRunning = true, OutputLimit = checked(source.FileSize * 2 + 16 * 1024 * 1024) };
        job._exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-copyts",
            "-protocol_whitelist", "fd,pipe", "-itsoffset", Seconds(-clip.InPts, source.Timing.TimeBase), "-i", "fd:",
            "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-t", Number((decimal)plan.DurationTicks / TimeSpan.TicksPerSecond),
            "-avoid_negative_ts", "disabled", "-f", "mp4", "-fd", "1", "fd:"];
        return job;
    }

    /// <summary>One clip, exact globally allocated frames. Output is an independently decodable H.264 stream.</summary>
    public static ProjectMediaJob EncodeClipVideo(ProjectRenderPlan plan, int index, long skipFrames = 0)
        => ComposeClipVideo(plan, index, skipFrames, false);

    private static ProjectMediaJob ComposeClipVideo(ProjectRenderPlan plan, int index, long skipFrames, bool preview)
    {
        var part = plan.Clips[index];
        var canvas = plan.Canvas;
        var count = part.EndFrame - part.StartFrame - skipFrames;
        if (skipFrames < 0) throw new ArgumentOutOfRangeException(nameof(skipFrames));
        if (count <= 0 || (!preview && (canvas.Width % 2 != 0 || canvas.Height % 2 != 0 ||
            canvas.Width > 4096 || canvas.Height > 4096)))
            throw new InvalidDataException("H.264 export requires a positive frame range and an even canvas up to 4096 pixels.");
        // Large project canvases remain valid for preview. Bound intermediate allocations;
        // this does not change the saved canvas or silently relax export limitations.
        if (preview && (long)canvas.Width * canvas.Height * 4 > MaximumFrameBytes)
        {
            var ratio = 2048d / Math.Max(canvas.Width, canvas.Height);
            canvas = canvas with { Width = Math.Max(2, (int)(canvas.Width * ratio)),
                Height = Math.Max(2, (int)(canvas.Height * ratio)) };
        }
        var rgbCanvas = preview && (canvas.Width % 2 != 0 || canvas.Height % 2 != 0);
        var clip = part.Clip;
        ValidateTimestamp(clip.InPts, part.Source.Timing.TimeBase);
        ValidateTimestamp(clip.OutPts, part.Source.Timing.TimeBase);
        var fps = $"{canvas.Fps.Numerator}/{canvas.Fps.Denominator}";
        var phase = (decimal)part.StartFrame * canvas.Fps.Denominator / canvas.Fps.Numerator -
            (decimal)part.ExactStart.Numerator / (decimal)part.ExactStart.Denominator;
        var origin = Seconds(clip.InPts, part.Source.Timing.TimeBase);
        var crop = Number(1 - clip.Crop / 100 * 2);
        // Crop is the existing symmetric per-edge percentage, not a silently reinterpreted rectangle.
        var effects = (rgbCanvas ? "format=rgba," : "") + $"crop=trunc(iw*{crop}/2)*2:trunc(ih*{crop}/2)*2," +
            $"scale={canvas.Width}:{canvas.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2," +
            $"pad={canvas.Width}:{canvas.Height}:(ow-iw)/2:(oh-ih)/2:color=black," +
            $"scale=trunc(iw*{Number(clip.Scale)}/2)*2:trunc(ih*{Number(clip.Scale)}/2)*2";
        var filter = $"[0:v:0]trim=start_pts={clip.InPts}:end_pts={clip.OutPts}," +
            $"setpts=PTS-({origin}+{Number(phase)})/TB,{effects}," +
            $"fps=fps={fps}:start_time=0:round=up,tpad=stop_mode=clone:stop_duration=1," +
            $"trim=start_frame={skipFrames}:end_frame={part.EndFrame - part.StartFrame},setpts=PTS-STARTPTS,setsar=1[v];" +
            $"color=c=black:s={canvas.Width}x{canvas.Height}:r={fps}," + (rgbCanvas ? "format=rgba," : "") + $"trim=end_frame={count}[bg];" +
            $"[bg][v]overlay=x=(W-w)/2+W*{Number(clip.PositionX / 100)}:y=(H-h)/2+H*{Number(clip.PositionY / 100)}:shortest=1:eof_action=endall" +
            (rgbCanvas ? ":format=rgb" : "") + "[out]";
        // Preserve the original sampling grid: shifting by a fractional source tick changes
        // fps rounding. Only seek demuxing; discard the prefix before overlay composition.
        var seekPts = skipFrames == 0 ? clip.InPts : checked(clip.InPts +
            (long)decimal.Floor((phase + (decimal)skipFrames * canvas.Fps.Denominator / canvas.Fps.Numerator) *
                part.Source.Timing.TimeBase.Denominator / part.Source.Timing.TimeBase.Numerator));
        var job = new ProjectMediaJob(seekPts, part.Source.Timing.TimeBase, 0, 0)
        { RequiresOutput = true, IsLongRunning = true,
            OutputLimit = checked(count * ((long)canvas.Width * canvas.Height * 4 + 65536)) };
        job._exportArguments = [..job.InputArguments(), "-filter_complex_threads", "1", "-filter_complex", filter,
            "-map", "[out]", "-an", "-sn", "-dn",
            "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-preset", "veryfast", "-crf", "18",
            "-bf", "0", "-pix_fmt", "yuv420p", "-threads", "2", "-f", "h264", "pipe:1"];
        return job;
    }

    /// <summary>Stereo PCM with common video origin, volume and clip-relative fades; never per-clip AAC.</summary>
    public static ProjectMediaJob RenderClipAudio(ProjectRenderPlan plan, int index)
    {
        var part = plan.Clips[index];
        var clip = part.Clip;
        var count = part.EndAudioSample - part.StartAudioSample;
        ValidateTimestamp(clip.InPts, part.Source.Timing.TimeBase);
        ValidateTimestamp(clip.OutPts, part.Source.Timing.TimeBase);
        if (count <= 0) throw new InvalidDataException("No output audio samples in clip.");
        var origin = (decimal)clip.InPts * part.Source.Timing.TimeBase.Numerator / part.Source.Timing.TimeBase.Denominator;
        var phase = (decimal)part.StartAudioSample / 48000 - (decimal)part.ExactStart.Numerator / (decimal)part.ExactStart.Denominator;
        var end = Seconds(clip.OutPts, part.Source.Timing.TimeBase);
        var filter = part.Source.AudioCodec is not null && !clip.Muted
            ? $"[0:a:0]atrim=start={Number(origin)}:end={end},asetpts=PTS-({Number(origin + phase)})/TB," +
                $"aresample=48000:first_pts=0,aformat=sample_fmts=flt:channel_layouts=stereo,volume={Number(clip.Volume)}"
            : "anullsrc=r=48000:cl=stereo";
        long FadeBoundary(ProjectExactTime boundary, long deltaTicks) => new ProjectExactTime(
            boundary.Numerator * TimeSpan.TicksPerSecond + deltaTicks * boundary.Denominator,
            boundary.Denominator * TimeSpan.TicksPerSecond).CeilingUnits(48000) - part.StartAudioSample;
        if (clip.FadeInTicks > 0)
            filter += $",afade=t=in:ss=0:ns={Math.Max(1, FadeBoundary(part.ExactStart, clip.FadeInTicks))}";
        if (clip.FadeOutTicks > 0)
        {
            var start = Math.Max(0, FadeBoundary(part.ExactEnd, -clip.FadeOutTicks));
            filter += $",afade=t=out:ss={start}:ns={Math.Max(1, count - start)}";
        }
        filter += $",apad=whole_len={count},atrim=end_sample={count}[a]";
        var job = new ProjectMediaJob(clip.InPts, part.Source.Timing.TimeBase, 0, 0)
        { RequiresOutput = true, IsLongRunning = true, OutputLimit = checked(count * 8), ExactLength = checked(count * 8) };
        job._exportArguments = [..job.InputArguments(), "-filter_complex", filter, "-map", "[a]", "-vn", "-sn", "-dn",
            "-ac", "2", "-ar", "48000", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"];
        return job;
    }

    public static ProjectMediaJob MuxEditedMp4(ProjectRenderPlan plan, long maximumBytes)
    {
        if (plan.FrameCount <= 0 || maximumBytes <= 0) throw new InvalidDataException("Empty export.");
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0)
        { InputCount = 2, RequiresOutput = true, IsLongRunning = true, OutputLimit = maximumBytes };
        // Input 0 is video; input descriptor 3 is contiguous stereo PCM. A single AAC encode
        // preserves sample continuity. The seekable descriptor permits a normal MP4 moov.
        job._exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-protocol_whitelist", "fd,pipe", "-r", $"{plan.Canvas.Fps.Numerator}/{plan.Canvas.Fps.Denominator}",
            "-f", "h264", "-i", "fd:", "-protocol_whitelist", "fd,pipe", "-f", "f32le", "-ar", "48000", "-ac", "2",
            "-fd", "3", "-i", "fd:", "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac",
            "-b:a", "192k", "-threads", "2", "-f", "mp4", "-fd", "1", "fd:"];
        return job;
    }

    public static ProjectMediaJob InspectEditedMp4()
    {
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0)
        { IsProbe = true, IsLongRunning = true, OutputLimit = MaximumDiagnosticBytes };
        job._exportArguments = ["-v", "error", "-protocol_whitelist", "fd,pipe", "-count_frames",
            "-show_entries", "stream=codec_type,codec_name,width,height,sample_rate,channels,nb_read_frames,duration:format=duration",
            "-of", "json", "fd:"];
        return job;
    }
}
