// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Capture;

/// <summary>The entire untrusted-input surface exposed by the isolated recording helper.</summary>
public sealed record ObsCaptureRequest(int Protocol, bool Synthetic, int Fps, int Width, int Height,
    int X, int Y, string Monitor, int MonitorWidth, int MonitorHeight, int CropX, int CropY,
    string Audio, string Microphone, string SystemDevice, bool Cursor, string Encoder, int Quality)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ObsCaptureRequest Create(RecordingConfiguration config, CaptureRegion bounds, MonitorInfo monitor,
        HardwareEncoderType encoder, bool synthetic)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(monitor);
        var display = monitor.Bounds;
        if (config.Fps is < 1 or > 120 || bounds.Width is < 2 or > 16384 || bounds.Height is < 2 or > 16384 ||
            bounds.Width % 2 != 0 || bounds.Height % 2 != 0 || display.Width <= 0 || display.Height <= 0 ||
            bounds.X < display.X || bounds.Y < display.Y ||
            (long)bounds.X + bounds.Width > (long)display.X + display.Width ||
            (long)bounds.Y + bounds.Height > (long)display.Y + display.Height ||
            string.IsNullOrWhiteSpace(monitor.DeviceName))
            throw new ArgumentException("The requested capture geometry or native display identity is unsupported.");
        var audio = config.IsRecoverySilenceMode || config.AudioSource == AudioSourceType.None && config.MaintainSegmentAudioTrack
            ? "silence" : config.AudioSource switch
            {
                AudioSourceType.None => "none", AudioSourceType.SystemOnly => "system",
                AudioSourceType.MicrophoneOnly => "microphone", AudioSourceType.SystemAndMicrophone => "both",
                _ => throw new ArgumentException("Unsupported audio source.")
            };
        var microphone = NormalizeMicrophoneIdentity(config.MicrophoneDeviceId, OperatingSystem.IsMacOS());
        var system = config.SystemAudioDeviceId;
        if (string.IsNullOrWhiteSpace(system) || system == "default_system") system = "default";
        return new(1, synthetic, config.Fps, bounds.Width, bounds.Height, bounds.X, bounds.Y,
            monitor.DeviceName, display.Width, display.Height, checked(bounds.X - display.X), checked(bounds.Y - display.Y),
            audio, microphone, system, config.CursorEffect is not (CursorEffectMode.Hidden or CursorEffectMode.PresentationStick),
            EncoderName(encoder), config.VideoQualityValue);
    }

    internal static string NormalizeMicrophoneIdentity(string? deviceId, bool isMacOS)
    {
        // MacOsAudioDeviceService historically exposed only "0", its AVFoundation
        // default-input placeholder. CoreAudio uses "default", not numeric indexes.
        // Preserve every explicit UID (and Windows identity) so missing devices fail closed.
        return string.IsNullOrWhiteSpace(deviceId) || deviceId == "default_mic" || isMacOS && deviceId == "0"
            ? "default" : deviceId;
    }

    public byte[] Serialize()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        if (bytes.Length > 65535) throw new ArgumentException("Recording helper request exceeds the protocol limit.");
        return bytes;
    }

    internal static string EncoderName(HardwareEncoderType encoder) => encoder switch
    {
        HardwareEncoderType.SoftwareCpu => "cpu", HardwareEncoderType.NvidiaNvenc => "nvenc",
        HardwareEncoderType.IntelQsv => "qsv", HardwareEncoderType.AmdAmf => "amf",
        HardwareEncoderType.AppleVideoToolbox => "apple",
        _ => throw new ArgumentException("An explicit supported encoder must be selected before capture.")
    };
}
