// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    public static string CreateConcatManifest(IReadOnlyList<ProjectSourceEvidence> sources)
    {
        if (sources.Count is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(sources));
        var manifest = new StringBuilder("ffconcat version 1.0\n");
        for (var i = 0; i < sources.Count; i++)
        {
            var video = sources[i].Video;
            var start = (decimal)video.FirstPts * video.TimeBase.Numerator / video.TimeBase.Denominator;
            if (sources[i].Audio is { } audio)
                start = Math.Min(start, (decimal)audio.FirstPts * audio.TimeBase.Numerator / audio.TimeBase.Denominator);
            var duration = ((decimal)video.EndPts - video.FirstPts) * video.TimeBase.Numerator / video.TimeBase.Denominator;
            if (duration <= 0) throw new InvalidDataException("Invalid concat duration.");
            manifest.Append("file 'fd:'\noption fd ").Append((i + 3).ToString(CultureInfo.InvariantCulture))
                .Append("\nduration ").Append(Number(duration)).Append('\n');
        }
        return manifest.ToString();
    }

    public static ProjectMediaJob ConcatWholeRecordings(IReadOnlyList<ProjectSourceEvidence> sources, long totalBytes, bool videoOnly = false)
    {
        if (sources.Count is < 2 or > 128 || totalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sources));
        // The concat demuxer preserves every encoded AAC frame. Per-segment priming or
        // padding therefore accumulates in decoded audio even if video durations are exact.
        // Require normalization before copying audio; the verified exporter renders this case.
        if (!videoOnly && sources.Any(source => source.Audio is { } audio &&
            (audio.Codec != "aac" || audio.SampleRate <= 0 || audio.PacketCount <= 0 ||
             audio.StartSeconds != source.Video.StartSeconds ||
             Math.Abs(audio.PacketCount * 1024d / audio.SampleRate -
                 (source.Video.EndSeconds - source.Video.StartSeconds)) > 1d / audio.SampleRate)))
            throw new ProjectRemuxIncompatibleException("Segment audio requires normalization before concatenation.");
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0) { InputCount = sources.Count + 1,
            RequiresOutput = true, IsLongRunning = true, OutputLimit = checked(totalBytes * 2 + 16 * 1024 * 1024) };
        var videoStart = (decimal)sources[0].Video.FirstPts * sources[0].Video.TimeBase.Numerator / sources[0].Video.TimeBase.Denominator;
        var firstStart = (decimal)sources[0].FormatStartSeconds;
        job._exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-copyts",
            "-protocol_whitelist", "fd,pipe", "-safe", "0", "-f", "concat", "-itsoffset", Number(firstStart - videoStart), "-i", "fd:",
            "-map", "0:v:0", ..(!videoOnly && sources[0].Audio is not null ? new[] { "-map", "0:a:0" } : new[] { "-an" }),
            "-c", "copy", "-avoid_negative_ts", "disabled", "-f", "mp4", "-fd", "1", "fd:"];
        return job;
    }

    public static ProjectMediaJob MuxCopiedVideoAndAudio(long outputLimit)
    {
        if (outputLimit <= 0) throw new ArgumentOutOfRangeException(nameof(outputLimit));
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0) { InputCount = 2, RequiresOutput = true,
            IsLongRunning = true, OutputLimit = outputLimit };
        job._exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-copyts",
            "-protocol_whitelist", "fd,pipe", "-i", "fd:", "-protocol_whitelist", "fd,pipe",
            "-f", "f32le", "-ar", "48000", "-ac", "2", "-fd", "3", "-i", "fd:",
            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-avoid_negative_ts", "disabled",
            "-f", "mp4", "-fd", "1", "fd:"];
        return job;
    }

    public static ProjectMediaJob RemuxWholeRecording(ProjectRenderPlan plan, bool convertAudio, bool hasAudio)
    {
        if (plan.Clips.Length != 1) throw new ArgumentException("Single source required.");
        var part = plan.Clips[0];
        ValidateTimestamp(part.Clip.InPts, part.Source.Timing.TimeBase);
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0) { RequiresOutput = true, IsLongRunning = true,
            OutputLimit = checked(part.Source.FileSize * 2 + plan.AudioSampleCount * 8 + 16 * 1024 * 1024) };
        job._exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-copyts",
            "-protocol_whitelist", "fd,pipe", "-itsoffset", Seconds(-part.Clip.InPts, part.Source.Timing.TimeBase), "-i", "fd:",
            "-map", "0:v:0", ..(hasAudio ? new[] { "-map", "0:a:0", "-c:a", convertAudio ? "aac" : "copy" } : new[] { "-an" }),
            "-c:v", "copy", "-avoid_negative_ts", "disabled", "-f", "mp4", "-fd", "1", "fd:"];
        return job;
    }

    public static ProjectMediaJob InspectPackets(long sourceLength)
    {
        if (sourceLength <= 0) throw new ArgumentOutOfRangeException(nameof(sourceLength));
        var job = new ProjectMediaJob(0, new(1, 1), 0, 0) { IsProbe = true, IsLongRunning = true,
            OutputLimit = checked(sourceLength * 32 + 1024 * 1024) };
        job._exportArguments = ["-v", "error", "-protocol_whitelist", "fd,pipe", "-i", "fd:",
            "-show_packets", "-show_streams", "-show_format", "-show_data_hash", "sha256", "-show_entries",
            "packet=stream_index,pts,dts,duration,flags:stream=index,codec_name,codec_type,width,height,pix_fmt,time_base,r_frame_rate,sample_rate,channels,channel_layout,extradata_hash:format=start_time",
            "-of", "compact=p=1:nk=0"];
        return job;
    }
}
