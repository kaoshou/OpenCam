using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectBookmarkTests
{
    [Fact]
    public async Task SavedBookmarksSurviveReopenWithoutChangingManifest()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-bookmarks-").FullName;
        try
        {
            var store = new JsonProjectStore(); string manifest;
            var id = Guid.NewGuid();
            await using (var h = await store.CreateAsync(root, "Bookmarks"))
            {
                manifest = Path.Combine(h.ProjectDirectory, "project.opencam");
                var before = await File.ReadAllBytesAsync(manifest);
                var doc = await ProjectBookmarkStore.ReadAsync(h);
                await ProjectBookmarkStore.WriteAsync(h, doc with { Revision = 1,
                    Bookmarks = [new(id, "session", "sessions/session/part.mkv", 10_000_000, "重點1")] }, 0);
                Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
            }
            await using var reopened = await store.OpenAsync(manifest);
            var saved = await ProjectBookmarkStore.ReadAsync(reopened);
            Assert.Equal(id, Assert.Single(saved.Bookmarks).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectBookmarkStore.WriteAsync(reopened, saved with { Revision = 2 }, 0));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptSidecarIsNeverReplacedByNewMarkers()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-bookmarks-").FullName;
        try
        {
            await using var h = await new JsonProjectStore().CreateAsync(root, "Bookmarks");
            var path = Path.Combine(h.ProjectDirectory, "opencam-bookmarks.json");
            await File.WriteAllTextAsync(path, "broken");
            await Assert.ThrowsAnyAsync<Exception>(() => ProjectBookmarkStore.ReadAsync(h));
            await Assert.ThrowsAnyAsync<Exception>(() => ProjectBookmarkStore.WriteAsync(h, new(1, h.Current.ProjectId, 1, []), 0));
            Assert.Equal("broken", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SourceRelativeMarkersFollowReorderAndHideTrimmedPositions()
    {
        var p = ProjectClipEditTests.Fixture(); var source = p.Sources[0];
        var b = new ProjectBookmark(Guid.NewGuid(), source.SessionId, source.RelativePath, 35_000_000, "重點1");
        // Source starts at 5000; 3.5s maps to PTS8500, half a second into clip B.
        Assert.Equal(25_000_000, Assert.Single(ProjectBookmarks.Project(p, b)).TimelineTicks);
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.Move(p.Clips[1].Id, p.Clips[0].Id));
        Assert.Equal(5_000_000, Assert.Single(ProjectBookmarks.Project(h.Current, b)).TimelineTicks);
        h.Apply(new ProjectClipEdit.Trim(p.Clips[1].Id, 9000, 11000));
        Assert.Empty(ProjectBookmarks.Project(h.Current, b));
        h.Undo(); Assert.Single(ProjectBookmarks.Project(h.Current, b));
    }

    [Fact]
    public void DuplicateAndCompoundClipsKeepEveryRetainedOccurrence()
    {
        var p = ProjectClipEditTests.Fixture(); var source = p.Sources[0];
        var b = new ProjectBookmark(Guid.NewGuid(), source.SessionId, source.RelativePath, 5_000_000, "One");
        var duplicated = p with { Clips = p.Clips.Add(p.Clips[0] with { Id = Guid.NewGuid() }) };
        Assert.Equal(new long?[] { 5_000_000, 55_000_000 }, ProjectBookmarks.Project(duplicated, b).Select(p => p.TimelineTicks));
        var history = new ProjectEditHistory(p);
        history.Apply(new ProjectClipEdit.Merge(p.Clips[1].Id, true));
        var compound = Assert.Single(ProjectBookmarks.Project(history.Current, b));
        Assert.Equal(history.Current.Clips[0].Id, compound.ClipId); Assert.Equal(5_000_000, compound.TimelineTicks);
    }

    [Fact]
    public void RejectsInvalidNamesOffsetsPathsAndDuplicateIds()
    {
        var id = Guid.NewGuid();
        var b = new ProjectBookmark(id, "session", "sessions/session/part.mkv", 0, "重點1");
        foreach (var bad in new[] { b with { OffsetTicks = -1 }, b with { RelativePath = "../outside.mkv" }, b with { Name = "" } })
            Assert.Throws<InvalidDataException>(() => ProjectBookmarks.Validate(new(1, Guid.NewGuid(), 0, [bad])));
        Assert.Throws<InvalidDataException>(() => ProjectBookmarks.Validate(new(1, Guid.NewGuid(), 0, [b, b])));
    }
}
