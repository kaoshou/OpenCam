// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectRational(long Numerator, long Denominator);
public sealed record ProjectCanvas(int Width, int Height, ProjectRational Fps);
public sealed record ProjectStreamTiming(ProjectRational TimeBase, long StartPts, long DurationTs);
public sealed record ProjectViewState(long PlayheadTicks = 0, bool LeftPanel = true,
    bool RightPanel = true, double Zoom = 1, double TimelineHeight = 240);

/// <summary>Durable edit recipe. Original source media is never rewritten by editing.</summary>
public sealed record RecordingProject
{
    public int SchemaVersion { get; init; } = 1;
    public Guid ProjectId { get; init; }
    public long Revision { get; init; }
    public string Name { get; init; } = "";
    public ProjectCanvas Canvas { get; init; } = new(1920, 1080, new(30, 1));
    public ImmutableArray<ProjectSource> Sources { get; init; } = [];
    public ImmutableArray<ProjectClip> Clips { get; init; } = [];
    public ImmutableArray<string> Sessions { get; init; } = [];
    public ProjectViewState ViewState { get; init; } = new();
    public bool AutoExportOnStop { get; init; } = true;
    public Guid? ResolvedDraftId { get; init; }
    // A preference-only save may advance Revision before a replacement draft commits.
    // Retain exactly the last acknowledged draft base across that durable transition.
    public long? RecoveryDraftBaseRevision { get; init; }
}

public sealed record ProjectSource
{
    public Guid Id { get; init; }
    public string SessionId { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public long FileSize { get; init; }
    public string Sha256 { get; init; } = "";
    public ProjectStreamTiming Timing { get; init; } = new(new(1, 1), 0, 0);
    public int Width { get; init; }
    public int Height { get; init; }
    public string VideoCodec { get; init; } = "";
    public string? AudioCodec { get; init; }
}

public sealed record ProjectClip
{
    public Guid Id { get; init; }
    public Guid SourceId { get; init; }
    public long InPts { get; init; }
    public long OutPts { get; init; }
    public string Name { get; init; } = "";
    public Guid? GroupId { get; init; }
    public double Volume { get; init; } = 1;
    public bool Muted { get; init; }
    public long FadeInTicks { get; init; }
    public long FadeOutTicks { get; init; }
    public double Crop { get; init; }
    public double Scale { get; init; } = 1;
    public double PositionX { get; init; }
    public double PositionY { get; init; }
}
