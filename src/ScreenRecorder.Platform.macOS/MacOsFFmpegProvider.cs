using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public class MacOsFFmpegProvider : IFFmpegPlatformProvider
{
    private readonly IDisplayService _displayService;

    public MacOsFFmpegProvider()
        : this(new MacOsDisplayService())
    {
    }

    public MacOsFFmpegProvider(IDisplayService displayService)
    {
        _displayService = displayService;
    }

    public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes()
    {
        return new List<(string, string, HardwareEncoderType)>
        {
            ("h264_videotoolbox", "-pix_fmt nv12", HardwareEncoderType.AppleVideoToolbox)
        };
    }

    public string BuildInputArguments(
        RecordingConfiguration config,
        int x,
        int y,
        int width,
        int height,
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
            var syntheticVideo =
                $"-f lavfi -i testsrc=size={width}x{height}:rate={config.Fps}";
            return includeAudioTrack
                ? syntheticVideo +
                  " -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000" +
                  " -map 0:v -map 1:a"
                : syntheticVideo;
        }

        var args = new List<string>
        {
            "-thread_queue_size 1024",
            "-f avfoundation",
            config.CursorEffect == CursorEffectMode.Hidden
                ? "-capture_cursor 0"
                : "-capture_cursor 1",
            $"-framerate {config.Fps}"
        };

        var monitors = _displayService.GetMonitors();
        var display = monitors.FirstOrDefault(monitor => monitor.Index == config.MonitorIndex) ??
                      monitors.FirstOrDefault(monitor => monitor.IsPrimary) ??
                      monitors.FirstOrDefault();
        var videoInput = "Capture screen " + (display?.Index ?? Math.Max(0, config.MonitorIndex));
        var requestsMicrophone = config.AudioSource is
            AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        var hasDirectMicrophone =
            requestsMicrophone &&
            hasDirectShowMic &&
            !config.IsRecoverySilenceMode &&
            !string.IsNullOrWhiteSpace(config.MicrophoneDeviceId);
        var hasNativeMicrophone =
            requestsMicrophone &&
            !config.IsRecoverySilenceMode &&
            !string.IsNullOrWhiteSpace(microphoneAudioPipeArg);
        var requestsSystemAudio = config.AudioSource is
            AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        var hasSystemAudio =
            requestsSystemAudio &&
            !config.IsRecoverySilenceMode &&
            !string.IsNullOrWhiteSpace(systemAudioPipeArg);

        args.Add($"-i \"{videoInput}:none\"");

        var nextInputIndex = 1;
        int? microphoneInputIndex = null;
        int? systemAudioInputIndex = null;

        if (hasNativeMicrophone)
        {
            microphoneInputIndex = nextInputIndex++;
            args.Add(microphoneAudioPipeArg!.Trim());
        }
        else if (hasDirectMicrophone)
        {
            microphoneInputIndex = nextInputIndex++;
            args.Add("-thread_queue_size 1024");
            args.Add("-f avfoundation");
            args.Add($"-i \":{config.MicrophoneDeviceId}\"");
        }

        if (hasSystemAudio)
        {
            systemAudioInputIndex = nextInputIndex++;
            args.Add(systemAudioPipeArg!.Trim());
        }

        if (config.IsRecoverySilenceMode)
        {
            args.Add("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000");
        }

        if (config.CaptureSource == CaptureSourceType.CustomRegion)
        {
            var localX = Math.Max(0, x - (display?.Bounds.X ?? 0));
            var localY = Math.Max(0, y - (display?.Bounds.Y ?? 0));
            args.Add($"-filter:v \"crop={width}:{height}:{localX}:{localY}\"");
        }
        else
        {
            // AVFoundation reports Retina screen frames in backing pixels
            // (for example 4480x2520) while the display service exposes the
            // logical capture size (2240x1260). Normalize monitor captures so
            // VideoToolbox receives the requested, encodable dimensions.
            args.Add(
                $"-filter:v \"scale={width}:{height}:flags=fast_bilinear\"");
        }

        if (includeAudioTrack)
        {
            if (config.IsRecoverySilenceMode ||
                (!microphoneInputIndex.HasValue && !systemAudioInputIndex.HasValue))
            {
                if (!config.IsRecoverySilenceMode)
                {
                    args.Add("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000");
                }

                args.Add(
                    "-filter_complex \"[1:a]aresample=48000:async=1:first_pts=0," +
                    "aformat=sample_rates=48000:channel_layouts=stereo[silent]\" " +
                    "-map 0:v -map \"[silent]\"");
            }
            else if (microphoneInputIndex.HasValue && systemAudioInputIndex.HasValue)
            {
                args.Add(
                    $"-filter_complex \"[{microphoneInputIndex}:a]aresample=48000:async=1:first_pts=0[mic];" +
                    $"[{systemAudioInputIndex}:a]aresample=48000:async=1:first_pts=0[sys];" +
                    "[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0," +
                    "aformat=sample_rates=48000:channel_layouts=stereo[aout]\" " +
                    "-map 0:v -map \"[aout]\"");
            }
            else if (microphoneInputIndex.HasValue)
            {
                args.Add(
                    $"-filter_complex \"[{microphoneInputIndex}:a]aresample=48000:async=1:first_pts=0," +
                    "aformat=sample_rates=48000:channel_layouts=stereo[mic]\" " +
                    "-map 0:v -map \"[mic]\"");
            }
            else
            {
                args.Add(
                    $"-filter_complex \"[{systemAudioInputIndex}:a]aresample=48000:async=1:first_pts=0," +
                    "aformat=sample_rates=48000:channel_layouts=stereo[sys]\" " +
                    "-map 0:v -map \"[sys]\"");
            }
        }

        return string.Join(" ", args);
    }

    public string BuildOutputArguments(RecordingConfiguration config, HardwareEncoderType encoderType, string outputPath)
    {
        var args = new List<string>();

        string videoCodec = encoderType switch
        {
            HardwareEncoderType.AppleVideoToolbox => "h264_videotoolbox",
            HardwareEncoderType.SoftwareCpu => "libx264",
            _ => "libx264"
        };
        args.Add($"-c:v {videoCodec}");

        if (videoCodec == "h264_videotoolbox")
        {
            args.Add("-profile:v main");
            args.Add("-pix_fmt nv12");
            args.Add($"-b:v {config.VideoBitrateKbps}k");
        }
        else
        {
            args.Add("-preset fast");
            args.Add("-pix_fmt yuv420p");
            args.Add($"-b:v {config.VideoBitrateKbps}k");
        }

        if (config.AudioSource != AudioSourceType.None ||
            config.MaintainSegmentAudioTrack)
        {
            args.Add(
                $"-c:a aac -ar 48000 -ac 2 -b:a {config.AudioBitrateKbps}k");
        }

        args.Add("-flush_packets 1 -cluster_time_limit 1000 -f matroska");
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }
}
