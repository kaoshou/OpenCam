// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum RecordingExportState { Idle, Running, Succeeded, Failed, Canceled }
public sealed record RecordingExportStatus(Guid ExportId, long Revision, RecordingExportState State,
    double Progress, string? FinalPath = null, string? Error = null);
public sealed record RecordingExportResult(bool Success, string? FinalPath, string? Error);
