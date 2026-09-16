using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.Models;

/// <summary>
/// 錄影作業設定參數
/// </summary>
public class RecordingConfiguration
{
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

    public int VideoBitrateKbps { get; set; } = 6000;

    public int AudioBitrateKbps { get; set; } = 192;

    public bool DeleteWorkingFileAfterSuccessfulRemux { get; set; } = false;

    public bool IsRecoverySilenceMode { get; set; } = false;

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
