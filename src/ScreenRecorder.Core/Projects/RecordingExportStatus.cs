// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum RecordingExportState { Idle, Running, Succeeded, Failed, Canceled }
public enum RecordingExportPhase { Inspecting, Copying, ConvertingAudio, Rendering, Verifying }
public interface IRecordingExportProgress : IProgress<double>
{
    void ReportPhase(RecordingExportPhase phase);
}
public sealed record RecordingExportStatus(Guid ExportId, long Revision, RecordingExportState State,
    double Progress, string? FinalPath = null, string? Error = null)
{
    public RecordingExportPhase Phase { get; init; } = RecordingExportPhase.Inspecting;
}
public sealed record RecordingExportResult(bool Success, string? FinalPath, string? Error);
