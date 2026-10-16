using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task EditorBookmarkUsesPlayheadSourceAndPersistsWithoutChangingEdits()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        var ipc = new ProjectIpcDispatcher(coordinator);
        async Task<ProjectReply> Send(string command, ProjectRequest r) => JsonSerializer.Deserialize<ProjectReply>((await ipc.DispatchAsync(
            new IpcMessage { MessageType = command, PayloadJson = JsonSerializer.Serialize(r) })).DataJson!)!;
        var state = (await Send("GetProjectStatus", new())).State;
        var before = coordinator.Current!;
        var request = new ProjectRequest { ProjectId = state.ProjectId, ServerInstanceId = state.ServerInstanceId,
            ExpectedRevision = state.Revision, OperationId = Guid.NewGuid(), TimelineTicks = 5_000_000, Name = "重點測試" };
        var added = await Send("AddProjectBookmarkAtPlayhead", request);
        Assert.True(added.Success, added.Error);
        Assert.True((await Send("AddProjectBookmarkAtPlayhead", request)).Success);
        var page = await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty });
        var mark = Assert.Single(page.Bookmarks!);
        Assert.Equal("重點測試", mark.Name);
        Assert.Equal(5_000_000, mark.TimelineTicks);
        Assert.Equal(5_000_000, mark.Bookmark.OffsetTicks);
        Assert.Equal(before, coordinator.Current);
        foreach (var invalid in new[] { request with { TimelineTicks = -1 }, request with { TimelineTicks = long.MaxValue },
            request with { ExpectedRevision = state.Revision + 1 }, request with { ServerInstanceId = Guid.NewGuid() },
            request with { BookmarkRevision = 0, Name = "Stale" }, request with { Name = "\n" } })
            Assert.False((await Send("AddProjectBookmarkAtPlayhead", invalid with { OperationId = Guid.NewGuid() })).Success);
        Assert.Equal(1, (await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty })).BookmarkCount);
        var sidecar = Path.Combine(coordinator.ProjectDirectory!, "opencam-bookmarks.json");
        var saved = JsonSerializer.Deserialize<ProjectBookmarkDocument>(await File.ReadAllTextAsync(sidecar), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("重點測試", Assert.Single(saved.Bookmarks).Name);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        var manifest = Path.Combine(coordinator.ProjectDirectory!, "project.opencam");
        Assert.True((await coordinator.CloseAsync()).Success);
        Assert.True((await coordinator.OpenAsync(manifest)).Success);
        var reopened = await coordinator.ReadBookmarksAsync(0, 100);
        Assert.Equal("重點測試", Assert.Single(reopened.Items).Name);
        Assert.Equal(5_000_000, reopened.Items[0].TimelineTicks);
    }

    [Theory]
    [InlineData(false, 2_500_000, 4_500_000)]
    [InlineData(true, 7_000_000, 1_000_000)]
    public async Task EditorBookmarkMapsTrimmedAndCompoundClips(bool compound, long playhead, long sourceOffset)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        if (compound)
        {
            Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        }
        var clip = coordinator.Current!.Clips[0];
        Assert.True((await coordinator.ApplyEditAsync(new ProjectClipEdit.Trim(clip.Id, 200, 800), coordinator.Current.Revision, Guid.NewGuid())).Success);
        if (compound)
            Assert.True((await coordinator.ApplyEditAsync(new ProjectClipEdit.Merge(coordinator.Current!.Clips[1].Id, true), coordinator.Current.Revision, Guid.NewGuid())).Success);
        var result = await coordinator.AddEditorBookmarkAsync(playhead, "Here", coordinator.Current!.Revision, 0, Guid.NewGuid());
        Assert.True(result.Success, result.ErrorCode);
        var mark = Assert.Single((await coordinator.ReadBookmarksAsync(0, 100)).Items);
        Assert.Equal(sourceOffset, mark.Bookmark.OffsetTicks);
        Assert.Equal(playhead, mark.TimelineTicks);
        Assert.Equal(coordinator.Current.Clips[0].Id, mark.ClipId);
        Assert.True((await coordinator.SaveAsync(coordinator.Current.Revision)).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task BookmarkIpcRecordsOnlyActiveCaptureAndDeduplicatesReplays()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        var ipc = new ProjectIpcDispatcher(coordinator);
        async Task<ProjectReply> Send(string command, ProjectRequest r) => JsonSerializer.Deserialize<ProjectReply>((await ipc.DispatchAsync(
            new IpcMessage { MessageType = command, PayloadJson = JsonSerializer.Serialize(r) })).DataJson!)!;
        var state = (await Send("GetProjectStatus", new())).State;
        var request = new ProjectRequest { ProjectId = state.ProjectId, ServerInstanceId = state.ServerInstanceId, OperationId = Guid.NewGuid() };
        Assert.True((await Send("AddProjectBookmark", request)).Success);
        Assert.True((await Send("AddProjectBookmark", request)).Success);
        var page = await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty });
        Assert.True(page.Success, page.Error);
        Assert.Equal(1, page.BookmarkCount);
        var marker = Assert.Single(page.Bookmarks!);
        Assert.Equal(10_000_000, marker.Bookmark.OffsetTicks);
        Assert.Equal("sessions/Sessions/" + scope.Recorder.CurrentSession!.SessionId + "/" + Path.GetFileName(scope.Recorder.CurrentSession.WorkingFilePath), marker.Bookmark.RelativePath);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        Assert.False((await Send("AddProjectBookmark", request with { OperationId = Guid.NewGuid() })).Success);
        Assert.True((await Send("RenameProjectBookmark", request with { OperationId = Guid.NewGuid(), BookmarkId = marker.Bookmark.Id, BookmarkRevision = page.BookmarkRevision, Name = "Example" })).Success);
        var renamed = await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty });
        Assert.Equal("Example", Assert.Single(renamed.Bookmarks!).Name);
        Assert.False((await Send("DeleteProjectBookmark", request with { OperationId = Guid.NewGuid(), BookmarkId = marker.Bookmark.Id, BookmarkRevision = page.BookmarkRevision })).Success);
        Assert.True((await Send("DeleteProjectBookmark", request with { OperationId = Guid.NewGuid(), BookmarkId = marker.Bookmark.Id, BookmarkRevision = renamed.BookmarkRevision })).Success);
        Assert.Equal(0, (await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty })).BookmarkCount);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await Send("AddProjectBookmark", request with { OperationId = Guid.NewGuid() })).Success);
        var resumed = Assert.Single((await Send("GetProjectBookmarks", request with { OperationId = Guid.Empty })).Bookmarks!);
        Assert.Equal(10_000_000, resumed.Bookmark.OffsetTicks);
        Assert.NotEqual(marker.Bookmark.RelativePath, resumed.Bookmark.RelativePath);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
    }
}
