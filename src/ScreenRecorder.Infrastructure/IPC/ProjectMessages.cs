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
    public long TimelineTicks { get; init; }
    public Guid? PlaybackGeneration { get; init; }
    public ProjectPreviewQuality PreviewQuality { get; init; } = ProjectPreviewQuality.P720;
    public bool PreviewMuted { get; init; }
    public bool RestoreDraft { get; init; }
    public bool AutoExportOnStop { get; init; } = true;
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
    public ProjectCanvas? Canvas { get; init; }
    public RecordingExportStatus? Export { get; init; }
    public static ProjectSnapshot Closed { get; } = new(null, "", null, 0, 0, ProjectMode.Closed, 0, false, false);
    public bool? HasUnsavedEdits { get; init; }
    public bool HasRecoverableDraft { get; init; }
    public string? DraftError { get; init; }
    public bool AutoExportOnStop { get; init; } = true;
    public bool IsDirty => HasUnsavedEdits ?? Revision != SavedRevision;
}

public sealed record ProjectReply(bool Success, string? Error, ProjectSnapshot State,
    ProjectClip[]? Clips = null, bool Unconfirmed = false, bool OperationKnown = true)
{
    public ProjectTimelineClip[]? TimelineClips { get; init; }
    public ProjectWaveformReply? Waveform { get; init; }
    public ProjectFrameReply? Frame { get; init; }
    public ProjectPlaybackState? Playback { get; init; }
    public string? ExistingOutputPath { get; init; }
}

public sealed record ProjectPlaybackState(Guid Generation, long TimelineTicks, bool Playing, string? Error)
{
    public bool Muted { get; init; }
}

/// <summary>Fixed-size RGBA still, sent exclusively over authenticated media IPC.</summary>
public sealed record ProjectFrameReply(long Revision, long TimelineTicks, Guid ClipId, byte[]? Rgba, string? Error = null)
{
    public const int Width = 512;
    public const int Height = 288;
    public const int ByteCount = Width * Height * 4;
    public int PixelWidth { get; init; } = Width;
    public int PixelHeight { get; init; } = Height;
    public static bool ValidGeometry(int width, int height, int bytes) => width is > 0 and <= 16384 &&
        height is > 0 and <= 16384 && bytes is > 0 and <= AuthenticatedMediaIpc.MaximumPixelBytes &&
        (long)width * height * 4 == bytes;
    public bool HasValidPixels => Rgba is not null && ValidGeometry(PixelWidth, PixelHeight, Rgba.Length);
}
