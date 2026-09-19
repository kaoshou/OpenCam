using ScreenRecorder.Core.Enums;
using System.Text.Json.Serialization;

namespace ScreenRecorder.Core.Models;

/// <summary>
/// 錄影作業設定參數
/// </summary>
public class RecordingConfiguration
{
    public const long DefaultDiskWarningThresholdBytes = 2L * 1024 * 1024 * 1024;
    public const long DefaultDiskCriticalThresholdBytes = 500L * 1024 * 1024;

    private string _videoQualityPreset = "Standard";

    public CaptureSourceType CaptureSource { get; set; } = CaptureSourceType.Monitor;

    public int MonitorIndex { get; set; } = 0;

    public CaptureRegion Region { get; set; } = CaptureRegion.Empty;

    public int Fps { get; set; } = 30;

    public AudioSourceType AudioSource { get; set; } = AudioSourceType.SystemAndMicrophone;

    public string? SystemAudioDeviceId { get; set; }

    public string? MicrophoneDeviceId { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public string EncoderName { get; set; } = "libx264";
    public HardwareEncoderType EncoderType { get; set; } = HardwareEncoderType.Auto;
    public CursorEffectMode CursorEffect { get; set; } = CursorEffectMode.Default;

    public string VideoQualityPreset
    {
        get => _videoQualityPreset;
        set => _videoQualityPreset = value?.Trim().ToUpperInvariant() switch
        {
            "ULTRA" => "Ultra",
            "COMPACT" => "Compact",
            _ => "Standard"
        };
    }

    [JsonIgnore]
    public int VideoQualityValue => VideoQualityPreset switch
    {
        "Ultra" => 18,
        "Compact" => 28,
        _ => 23
    };

    [JsonIgnore]
    public int VideoBitrateKbps => VideoQualityPreset switch
    {
        "Ultra" => 10000,
        "Compact" => 3500,
        _ => 6000
    };

    public int AudioBitrateKbps { get; set; } = 192;

    public bool DeleteWorkingFileAfterSuccessfulRemux { get; set; } = false;

    public long DiskWarningThresholdBytes { get; set; } = DefaultDiskWarningThresholdBytes;

    public long DiskCriticalThresholdBytes { get; set; } = DefaultDiskCriticalThresholdBytes;

    public bool IsRecoverySilenceMode { get; set; } = false;

    public bool MaintainSegmentAudioTrack { get; set; } = false;

    public void NormalizeDiskGuardThresholds()
    {
        if (DiskWarningThresholdBytes <= 0 ||
            DiskCriticalThresholdBytes <= 0 ||
            DiskWarningThresholdBytes <= DiskCriticalThresholdBytes)
        {
            DiskWarningThresholdBytes = DefaultDiskWarningThresholdBytes;
            DiskCriticalThresholdBytes = DefaultDiskCriticalThresholdBytes;
        }
    }

    public void ApplyPausedSettings(RecordingConfiguration updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        AudioSource = updates.AudioSource;
        SystemAudioDeviceId = updates.SystemAudioDeviceId;
        MicrophoneDeviceId = updates.MicrophoneDeviceId;
        CursorEffect = updates.CursorEffect;
        IsRecoverySilenceMode = false;
    }
}
