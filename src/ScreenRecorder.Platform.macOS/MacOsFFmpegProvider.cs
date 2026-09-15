using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public class MacOsFFmpegProvider : IFFmpegPlatformProvider
{
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

        string videoInput = "1";
        string audioInput = "none";
        
        // Use generic condition to check if mic is requested
        if (config.AudioSource.ToString().Contains("Microphone"))
        {
            audioInput = "0"; 
        }
        
        args.Add($"-i \"{videoInput}:{audioInput}\"");

        if (config.IsRecoverySilenceMode)
        {
            args.Add("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100");
        }

        if (config.CaptureSource == CaptureSourceType.CustomRegion)
        {
            args.Add($"-filter:v \"crop={width}:{height}:{x}:{y}\"");
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
