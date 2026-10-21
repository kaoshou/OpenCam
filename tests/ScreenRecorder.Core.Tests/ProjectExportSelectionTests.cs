using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectExportSelectionTests
{
    private static RecordingProject Project()
    {
        var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "session", RelativePath = "sources/a.mkv",
            FileSize = 5, Sha256 = new string('a', 64), Width = 100, Height = 100, VideoCodec = "h264",
            Timing = new(new(1, 1000), 0, 10000) };
        var group = Guid.NewGuid();
        return new() { SchemaVersion = 3, ProjectId = Guid.NewGuid(), Revision = 8, Name = "test",
            Sessions = ["session"], Sources = [source], Clips = [
                new() { Id = Guid.NewGuid(), SourceId = source.Id, Name = "first", InPts = 1000, OutPts = 2500, GroupId = group },
                new() { Id = Guid.NewGuid(), SourceId = source.Id, Name = "second", InPts = 4000, OutPts = 6000, GroupId = group }] };
    }

    [Fact]
    public void SelectingGroupedClipKeepsOnlyItsEditedRangeAndDoesNotMutateProject()
    {
        var original = Project();
        var before = JsonSerializer.Serialize(original);
        var selected = ProjectExportSelection.Create(original, new(Scope: ProjectExportScope.Clip, ClipId: original.Clips[0].Id));
        var clip = Assert.Single(selected.Clips);
        Assert.Equal(original.Clips[0].Id, clip.Id);
        Assert.Equal(1000, clip.InPts);
        Assert.Equal(2500, clip.OutPts);
        Assert.Equal(original.ProjectId, selected.ProjectId);
        Assert.Equal(8, selected.Revision);
        Assert.Equal(original.Canvas, selected.Canvas);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Assert.Equal(15_000_000, ProjectTimeline.Build(selected).DurationTicks);
    }

    [Fact]
    public void SelectingCompoundKeepsAllRetainedPartsButRejectsItsLeafAsTarget()
    {
        var p = Project();
        var merged = ProjectClipProjection.Combine(p, p.Clips[0], p.Clips[1]);
        p = p with { Clips = [merged] };
        var selected = ProjectExportSelection.Create(p, new(Scope: ProjectExportScope.Clip, ClipId: merged.Id));
        Assert.Equal(2, Assert.Single(selected.Clips).Parts.Length);
        Assert.Equal(35_000_000, ProjectTimeline.Build(selected).DurationTicks);
        Assert.Throws<InvalidDataException>(() => ProjectExportSelection.Create(p,
            new(Scope: ProjectExportScope.Clip, ClipId: merged.Parts[0].Id)));
    }

    [Fact]
    public void InvalidOrMissingTargetDoesNotFallBackToWholeProject()
    {
        var p = Project();
        foreach (var id in new Guid?[] { null, Guid.Empty, Guid.NewGuid() })
            Assert.Throws<InvalidDataException>(() => ProjectExportSelection.Create(p,
                new(Scope: ProjectExportScope.Clip, ClipId: id)));
    }

    [Fact]
    public void InvalidOptionsAreRejectedBeforeProjection()
    {
        var p = Project();
        ProjectExportOptions[] invalid = [new(Format: (ProjectExportFormat)99), new(Scope: (ProjectExportScope)99),
            new(ClipId: p.Clips[0].Id), new(Gif: new()), new(ProjectExportFormat.Gif, Gif: new(0)),
            new(ProjectExportFormat.Gif, Gif: new(800)), new(ProjectExportFormat.Gif, Gif: new(640, 30))];
        foreach (var options in invalid)
            Assert.Throws<InvalidDataException>(() => ProjectExportSelection.Create(p, options));
    }

    [Fact]
    public void LegacyRequestAndNewRequestRoundTripWithoutChangingScope()
    {
        Assert.Null(JsonSerializer.Deserialize<ProjectRequest>("{\"Path\":\"movie.mp4\"}")!.ExportOptions);
        var options = new ProjectExportOptions(ProjectExportFormat.Gif, ProjectExportScope.Clip, Guid.NewGuid(), new(null, 20));
        var request = new ProjectRequest { ExportOptions = options };
        Assert.Equal(options, JsonSerializer.Deserialize<ProjectRequest>(JsonSerializer.Serialize(request))!.ExportOptions);
    }

    [Fact]
    public void FingerprintsSeparateScopeAndGifOptionsButPreserveLegacyWholeMp4()
    {
        var p = Project();
        var legacy = ProjectExportFingerprint.Compute(p, ProjectExportFingerprint.Profile);
        Assert.Equal(legacy, ProjectExportFingerprint.Compute(p, new ProjectExportOptions()));
        var clipOptions = new ProjectExportOptions(Scope: ProjectExportScope.Clip, ClipId: p.Clips[0].Id);
        var selected = ProjectExportSelection.Create(p, clipOptions);
        Assert.NotEqual(ProjectExportFingerprint.Compute(selected, ProjectExportFingerprint.Profile),
            ProjectExportFingerprint.Compute(p, clipOptions));
        var gif = new ProjectExportOptions(ProjectExportFormat.Gif, Gif: new());
        var hash = ProjectExportFingerprint.Compute(p, gif);
        Assert.NotEqual(legacy, hash);
        Assert.NotEqual(hash, ProjectExportFingerprint.Compute(p, gif with { Gif = new(480, 15) }));
        Assert.NotEqual(hash, ProjectExportFingerprint.Compute(p, gif with { Gif = new(640, 20) }));
        Assert.NotEqual(hash, ProjectExportFingerprint.Compute(p, gif with { Scope = ProjectExportScope.Clip, ClipId = p.Clips[0].Id }));
    }
}
