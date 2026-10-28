// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    public static ProjectMediaJob VerifyImport(long maximumProgressBytes) => new(0, new(1, 1), 0, 0)
    {
        IsLongRunning = true, OutputLimit = maximumProgressBytes,
        _exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-err_detect", "explode",
            "-protocol_whitelist", "fd,pipe", "-i", "fd:", "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn",
            "-threads", "2", "-progress", "pipe:1", "-stats_period", "1", "-f", "null", "pipe:1"]
    };

    public static ProjectMediaJob InspectImport() => new(0, new(1, 1), 0, 0)
    {
        IsProbe = true, IsLongRunning = true, OutputLimit = MaximumDiagnosticBytes,
        _exportArguments = ["-v", "error", "-protocol_whitelist", "fd", "-i", "fd:",
            "-show_streams", "-show_format", "-of", "json"]
    };

    public static ProjectMediaJob PrepareImport(long outputLimit, bool convertVideo, bool convertAudio) => new(0, new(1, 1), 0, 0)
    {
        RequiresOutput = true, IsLongRunning = true, OutputLimit = outputLimit,
        _exportArguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-copyts",
            "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn",
            "-map_metadata", "-1", "-map_chapters", "-1",
            ..(convertVideo ? new[] { "-vf", "scale=trunc(iw*sar/2)*2:trunc(ih/2)*2,setsar=1",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-threads", "2" }
                : new[] { "-c:v", "copy" }),
            ..(convertAudio ? new[] { "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2" }
                : new[] { "-c:a", "copy" }),
            "-avoid_negative_ts", "make_non_negative", "-f", "matroska", "-fd", "1", "fd:"]
    };
}
