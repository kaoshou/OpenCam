// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.IPC;

public sealed record ProjectRequest
{
    public Guid? ServerInstanceId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid OperationId { get; init; }
    public Guid? ExportId { get; init; }
    public long ExpectedRevision { get; init; }
    public string? Path { get; init; }
    public string? Name { get; init; }
    public Guid ClipId { get; init; }
    public ProjectClipEdit? Edit { get; init; }
    public RecordingConfiguration? Configuration { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record ProjectSnapshot(Guid? ProjectId, string Name, string? Directory,
    long Revision, long SavedRevision, ProjectMode Mode, int ClipCount, bool CanUndo, bool CanRedo,
    string? LastError = null)
{
    public Guid? ServerInstanceId { get; init; }
    public bool NeedsRecoveryConfirmation { get; init; }
    public RecordingExportStatus? Export { get; init; }
    public static ProjectSnapshot Closed { get; } = new(null, "", null, 0, 0, ProjectMode.Closed, 0, false, false);
    public bool IsDirty => Revision != SavedRevision;
}

public sealed record ProjectReply(bool Success, string? Error, ProjectSnapshot State,
    ProjectClip[]? Clips = null, bool Unconfirmed = false, bool OperationKnown = true)
{
    public ProjectTimelineClip[]? TimelineClips { get; init; }
}
