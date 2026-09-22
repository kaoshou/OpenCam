// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows;

public class WindowsFFmpegProvider : IFFmpegPlatformProvider
{
    public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes()
    {
        yield return ("h264_nvenc", "-pix_fmt yuv420p", HardwareEncoderType.NvidiaNvenc);
        yield return ("h264_qsv", "-pix_fmt nv12", HardwareEncoderType.IntelQsv);
        yield return ("h264_amf", "-pix_fmt yuv420p", HardwareEncoderType.AmdAmf);
    }

    public string BuildInputArguments(
        RecordingConfiguration config, 
        int x, int y, int width, int height, 
        bool useSynthetic, 
        bool hasDirectShowMic,
        string? systemAudioPipeArg = null,
        string? microphoneAudioPipeArg = null)
    {
        var includeAudioTrack =
            config.AudioSource != AudioSourceType.None ||
            config.MaintainSegmentAudioTrack;

        if (useSynthetic)
        {
            return config.AudioSource switch
            {
                AudioSourceType.None when !includeAudioTrack =>
                    $"-y -f lavfi -i testsrc=size={width}x{height}:rate={config.Fps} ",

                AudioSourceType.None =>
                    $"-y -f lavfi -i testsrc=size={width}x{height}:rate={config.Fps} " +
                    "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ",

                AudioSourceType.SystemOnly or AudioSourceType.MicrophoneOnly =>
                    $"-y -f lavfi -i testsrc=size={width}x{height}:rate={config.Fps} " +
                    $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ",

                AudioSourceType.SystemAndMicrophone =>
                    $"-y -f lavfi -i testsrc=size={width}x{height}:rate={config.Fps} " +
                    $"-f lavfi -i sine=frequency=440:sample_rate=48000 " +
                    $"-f lavfi -i sine=frequency=880:sample_rate=48000 " +
                    $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[aout]\" -map 0:v -map \"[aout]\" ",

                _ => $"-y -f lavfi -i testsrc=size={width}x{height}:rate={config.Fps} "
            };
        }

        var drawMouseParam = config.CursorEffect == CursorEffectMode.Hidden ? "-draw_mouse 0 " : "-draw_mouse 1 ";
        var baseVideo = $"-y -rtbufsize 100M -f gdigrab {drawMouseParam}-framerate {config.Fps} -offset_x {x} -offset_y {y} -video_size {width}x{height} -i desktop ";

        if (config.AudioSource == AudioSourceType.None && !includeAudioTrack)
        {
            return baseVideo;
        }

        if (config.AudioSource == AudioSourceType.None)
        {
            return baseVideo +
                "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ";
        }

        if (config.IsRecoverySilenceMode)
        {
            return baseVideo + $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ";
        }

        bool hasSysAudio = !string.IsNullOrWhiteSpace(systemAudioPipeArg);

        switch (config.AudioSource)
        {
            case AudioSourceType.SystemAndMicrophone:
                if (hasSysAudio && hasDirectShowMic)
                {
                    // 0:v = desktop, 1:a = system audio pipe, 2:a = directshow mic
                    return baseVideo +
                           systemAudioPipeArg +
                           $"-f dshow -i audio=\"{config.MicrophoneDeviceId}\" " +
                           $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[aout]\" -map 0:v -map \"[aout]\" ";
                }
                else if (hasSysAudio)
                {
                    return baseVideo + systemAudioPipeArg;
                }
                else if (hasDirectShowMic)
                {
                    return baseVideo + $"-f dshow -i audio=\"{config.MicrophoneDeviceId}\" ";
                }
                else
                {
                    return baseVideo + $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ";
                }

            case AudioSourceType.SystemOnly:
                if (hasSysAudio)
                {
                    return baseVideo + systemAudioPipeArg;
                }
                else
                {
                    return baseVideo + $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ";
                }

            case AudioSourceType.MicrophoneOnly:
                if (hasDirectShowMic)
                {
                    return baseVideo + $"-f dshow -i audio=\"{config.MicrophoneDeviceId}\" ";
                }
                else
                {
                    return baseVideo + $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 ";
                }

            default:
                return baseVideo;
        }
    }

    public string BuildOutputArguments(RecordingConfiguration config, HardwareEncoderType encoderType, string workingFilePath)
    {
        var quality = config.VideoQualityValue;
        var vCodec = encoderType switch
        {
            HardwareEncoderType.NvidiaNvenc => $"-c:v h264_nvenc -preset p4 -cq {quality} -pix_fmt yuv420p",
            HardwareEncoderType.IntelQsv => $"-c:v h264_qsv -global_quality {quality} -pix_fmt nv12",
            HardwareEncoderType.AmdAmf => $"-c:v h264_amf -quality speed -rc cqp -qp_p {quality} -pix_fmt yuv420p",
            _ => $"-c:v libx264 -preset veryfast -crf {quality} -pix_fmt yuv420p"
        };

        if (config.AudioSource == AudioSourceType.None &&
            !config.MaintainSegmentAudioTrack)
        {
            return $"{vCodec} -flush_packets 1 -cluster_time_limit 1000 -f matroska \"{workingFilePath}\"";
        }

        return $"{vCodec} -c:a aac -ar 48000 -ac 2 -b:a {config.AudioBitrateKbps}k " +
               $"-flush_packets 1 -cluster_time_limit 1000 -f matroska \"{workingFilePath}\"";
    }
}
