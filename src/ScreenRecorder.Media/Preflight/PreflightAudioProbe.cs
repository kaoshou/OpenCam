// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Preflight;

public sealed class PreflightAudioProbe(Func<IProjectMediaProcess> inspect, Func<IProjectStreamingMediaProcess> decode)
{
    public async Task<TimeSpan> ValidateAsync(Stream source, CancellationToken ct)
    {
        if (source is not FileStream file || !file.CanRead || file.CanWrite || !file.CanSeek)
            throw new InvalidDataException("A bound read-only trial file is required.");
        var result = await inspect().RunAsync(ProjectMediaJob.InspectPreflightAudio(), [file], null, ct);
        using var json = JsonDocument.Parse(result.Output);
        var streams = json.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1 ||
            streams[0].GetProperty("codec_type").GetString() != "audio" ||
            streams[0].GetProperty("codec_name").GetString() != "pcm_s16le")
            throw new InvalidDataException("Trial must contain only the captured PCM audio.");
        long bytes = 0;
        await decode().RunStreamingAsync(ProjectMediaJob.DecodePreflightAudio(), file, async (pcm, token) =>
        {
            var buffer = new byte[8192];
            int count;
            while ((count = await pcm.ReadAsync(buffer, token)) > 0)
            {
                bytes = checked(bytes + count);
                if (bytes > 60L * 48000 * 8) throw new InvalidDataException("Trial audio exceeds its duration limit.");
            }
        }, ct);
        if (bytes % 8 != 0 || bytes < 4.5 * 48000 * 8)
            throw new InvalidDataException("Trial audio is incomplete or too short.");
        return TimeSpan.FromSeconds(bytes / (48000d * 8));
    }
}
