using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.Models;

/// <summary>
/// 獨立錄影工作階段元資料 (持久化於 session.json)
/// </summary>
public class RecordingSession
{
    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public DateTimeOffset LastHeartbeatTime { get; set; }

    public RecordingState State { get; set; } = RecordingState.Idle;

    public RecordingConfiguration Configuration { get; set; } = new();

    public int OutputWidth { get; set; }

    public int OutputHeight { get; set; }

    public string WorkingDirectory { get; set; } = string.Empty;

    public string WorkingFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 若錄影中途有暫停/繼續，記錄所有已錄製的分段 MKV 檔案路徑清單
    /// </summary>
    public List<string> SegmentFilePaths { get; set; } = new();

    public string FinalFilePath { get; set; } = string.Empty;

    public string LogFilePath { get; set; } = string.Empty;

    public long TotalVideoFramesRecorded { get; set; }

    public long TotalAudioSamplesRecorded { get; set; }

    public long DroppedFrames { get; set; }

    public long FileSizeBytes { get; set; }

    public string? StopReason { get; set; }

    public string? ErrorMessage { get; set; }
}
