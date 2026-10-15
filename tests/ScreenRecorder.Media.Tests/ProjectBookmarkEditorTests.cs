using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectBookmarkEditorTests
{
    [Fact]
    public async Task PendingBookmarkSaveDoesNotDisableStoppingRecording()
    {
        var client = new PendingClient(); var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        var save = vm.AddRecordingBookmarkAsync("Bookmark");
        try { Assert.False(vm.IsBusy); Assert.True(vm.CanFinish); }
        finally { client.Reply.SetResult(new(true, null, client.State)); await save; }
    }
    private sealed class PendingClient : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Test", "/tmp", 0, 0, ProjectMode.Recording, 0, false, false) { ServerInstanceId = Guid.NewGuid() };
        public TaskCompletionSource<ProjectReply> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default) => Reply.Task;
    }
    [Fact]
    public async Task SelectingMarkerSeeksAndRenameDeleteUseIndependentRevision()
    {
        var client = new Client(); var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        await vm.RefreshAsync();
        await vm.RefreshBookmarksAsync();
        vm.SelectedBookmark = Assert.Single(vm.Bookmarks);
        Assert.Equal(20_000_000, vm.PlayheadTicks);
        vm.BookmarkName = "New"; await vm.RenameBookmarkAsync();
        Assert.Equal("RenameProjectBookmark", client.Mutation);
        Assert.Equal(7, client.Request!.BookmarkRevision);
        await vm.DeleteBookmarkAsync(); Assert.Equal("DeleteProjectBookmark", client.Mutation);
    }
    private sealed class Client : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Test", "/tmp", 0, 0, ProjectMode.Ready, 1, false, false) { ServerInstanceId = Guid.NewGuid() };
        public string? Mutation; public ProjectRequest? Request;
        private readonly ProjectBookmark _marker = new(Guid.NewGuid(), "session", "sessions/session/part.mkv", 20_000_000, "One");
        private readonly Guid _clip = Guid.NewGuid();
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "GetProjectClips") return Task.FromResult(new ProjectReply(true, null, State, [new() { Id = _clip, Name = "Clip" }]) { TimelineClips = [new(_clip, 0, 40_000_000)] });
            if (command is "RenameProjectBookmark" or "DeleteProjectBookmark") { Mutation = command; Request = request; }
            return Task.FromResult(new ProjectReply(true, null, State) { Bookmarks = [new(_marker, Guid.NewGuid(), 20_000_000)], BookmarkCount = 1, BookmarkRevision = 7 });
        }
    }
}
