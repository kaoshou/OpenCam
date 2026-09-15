using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.Models;

/// <summary>
/// 錄影中即時遙測狀態 (UI 與 Recorder 間週期傳遞)
/// </summary>
public class RecorderTelemetry
{
    public string SessionId { get; set; } = string.Empty;

    public RecordingState State { get; set; } = RecordingState.Idle;

    public TimeSpan ElapsedTime { get; set; }

    public string WorkingFilePath { get; set; } = string.Empty;

    public string FinalFilePath { get; set; } = string.Empty;

    public long CurrentFileSizeBytes { get; set; }

    public long AvailableDiskSpaceBytes { get; set; }

    public double CurrentFps { get; set; }

    public long DroppedFrames { get; set; }

    public bool IsVideoCaptureHealthy { get; set; } = true;

    public bool IsSystemAudioHealthy { get; set; } = true;

    public bool IsMicrophoneHealthy { get; set; } = true;

    public bool IsEncoderHealthy { get; set; } = true;

    public string? LastError { get; set; }
}
