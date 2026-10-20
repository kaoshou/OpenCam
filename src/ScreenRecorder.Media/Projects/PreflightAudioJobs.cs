// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    public static ProjectMediaJob InspectPreflightAudio() => new(0, new(1, 48000), 0, 0)
    {
        IsProbe = true, IsLongRunning = true, OutputLimit = 16384,
        _exportArguments = ["-v", "error", "-protocol_whitelist", "fd,pipe", "-f", "matroska", "-i", "fd:",
            "-show_entries", "stream=codec_type,codec_name", "-of", "json"]
    };

    public static ProjectMediaJob DecodePreflightAudio(long skipSamples = 0, long? sampleCount = null)
    {
        if (skipSamples is < 0 or > 60 * 48000 || sampleCount is <= 0 or > 60 * 48000)
            throw new ArgumentOutOfRangeException(nameof(skipSamples));
        var trim = FormattableString.Invariant($"atrim=start_sample={skipSamples}") +
            (sampleCount is { } count ? FormattableString.Invariant($":end_sample={skipSamples + count}") : "") +
            ",asetpts=PTS-STARTPTS";
        return new(0, new(1, 48000), 0, 0)
        {
            IsLongRunning = true, OutputLimit = (sampleCount ?? 60 * 48000) * 8,
            _exportArguments = ["-nostdin", "-hide_banner", "-v", "error", "-xerror",
                "-protocol_whitelist", "fd,pipe", "-f", "matroska", "-i", "fd:",
                "-map", "0:a:0", "-vn", "-sn", "-dn", "-af", "aresample=48000," + trim,
                "-ar", "48000", "-ac", "2", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"]
        };
    }
}
