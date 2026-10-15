using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
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
