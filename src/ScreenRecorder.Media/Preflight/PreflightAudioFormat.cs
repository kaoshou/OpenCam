// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Preflight;

public static class PreflightAudioFormat
{
    public static string OutputArguments(bool recordTrial) =>
        "-vn -c:a pcm_s16le -ar 48000 -ac 2 " +
        (recordTrial ? "-flush_packets 1 -cluster_time_limit 1000 -f matroska pipe:1" : "-f null -");

    public static string BuildInput(RecordingConfiguration configuration, bool synthetic,
        string? system, string? microphone, bool mac)
    {
        var mode = configuration.AudioSource;
        if (mode == AudioSourceType.None || !Enum.IsDefined(mode) || configuration.IsRecoverySilenceMode)
            throw new InvalidOperationException("Audio confirmation requires selected, available audio sources.");
        var useSystem = mode is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        var useMic = mode is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        if (synthetic)
        {
            system = "-re -f lavfi -i sine=frequency=440:sample_rate=48000";
            microphone = "-re -f lavfi -i sine=frequency=880:sample_rate=44100";
        }
        if (useSystem && string.IsNullOrWhiteSpace(system) || useMic && string.IsNullOrWhiteSpace(microphone))
            throw new InvalidOperationException("A selected audio source is unavailable.");
        if (useSystem && useMic)
        {
            // Indices belong to this audio-only command, never the video pipeline.
            var filters = mac
                ? "[0:a]aresample=48000:async=1:first_pts=0[sys];[1:a]aresample=48000:async=1:first_pts=0[mic];" +
                  "[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0,aformat=sample_rates=48000:channel_layouts=stereo[aout]"
                : "[0:a][1:a]amix=inputs=2:duration=longest[aout]";
            return $"{system!.Trim()} {microphone!.Trim()} -filter_complex \"{filters}\" -map \"[aout]\"";
        }
        var input = useSystem ? system : microphone;
        return input!.Trim() + (mac
            ? " -map 0:a -af \"aresample=48000:async=1:first_pts=0,aformat=sample_rates=48000:channel_layouts=stereo\""
            : " -map 0:a");
    }
}
