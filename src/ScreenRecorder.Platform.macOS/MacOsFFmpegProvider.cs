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

    public string BuildInputArguments(RecordingConfiguration config, int x, int y, int width, int height, bool useSynthetic, bool hasDirectShowMic, string? systemAudioPipeArg = null)
    {
        if (useSynthetic)
        {
            return $"-f lavfi -i testsrc=size={width}x{height}:rate={config.Fps}";
        }

        var args = new List<string>
        {
            "-f avfoundation",
            $"-framerate {config.Fps}"
        };

        var monitors = _displayService.GetMonitors();
        var display = monitors.FirstOrDefault(monitor => monitor.Index == config.MonitorIndex) ??
                      monitors.FirstOrDefault(monitor => monitor.IsPrimary) ??
                      monitors.FirstOrDefault();
        var videoInput = "Capture screen " + (display?.Index ?? Math.Max(0, config.MonitorIndex));
        var requestsMicrophone = config.AudioSource is
            AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        var audioInput = requestsMicrophone && hasDirectShowMic &&
                         !string.IsNullOrWhiteSpace(config.MicrophoneDeviceId)
            ? config.MicrophoneDeviceId
            : "none";

        args.Add($"-i \"{videoInput}:{audioInput}\"");

        if (config.IsRecoverySilenceMode)
        {
            args.Add("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100");
        }

        if (config.CaptureSource == CaptureSourceType.CustomRegion)
        {
            var localX = Math.Max(0, x - (display?.Bounds.X ?? 0));
            var localY = Math.Max(0, y - (display?.Bounds.Y ?? 0));
            args.Add($"-filter:v \"crop={width}:{height}:{localX}:{localY}\"");
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
            args.Add($"-b:v {config.VideoBitrateKbps}k");
        }
        else
        {
            args.Add("-preset fast");
            args.Add($"-b:v {config.VideoBitrateKbps}k");
        }

        if (config.AudioSource.ToString() != "None")
        {
            args.Add($"-c:a aac -b:a {config.AudioBitrateKbps}k");
        }

        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }
}
