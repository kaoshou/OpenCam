// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPreviewPresenterTests
{
    [Fact]
    public async Task ThumbnailComesFromClipStartAndIsClearedByRevision()
    {
        var client = new PreviewClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var poll = vm.PollThumbnailAsync();
        client.Complete(0);
        await poll;
        Assert.Single(vm.Thumbnails);
        Assert.Equal(ProjectFrameReply.ByteCount, vm.Thumbnails[client.Clip.Id].Rgba!.Length);
        vm.ApplyReply(new(true, null, client.State with { Revision = 1 }));
        Assert.Empty(vm.Thumbnails);
    }

    [Fact]
    public async Task RealBytesPublishButOldSeekAndRevisionNeverPublish()
    {
        var client = new PreviewClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var first = vm.PollPreviewAsync();
        vm.Seek(100);
        client.Complete(0);
        await first;
        Assert.Null(vm.PreviewFrame);
        client.Pending = new();
        var next = vm.PollPreviewAsync();
        client.Complete(100);
        await next;
        Assert.Equal(ProjectFrameReply.ByteCount, vm.PreviewFrame!.Rgba!.Length);
        vm.ApplyReply(new(true, null, client.State with { Revision = 1 }));
        Assert.Null(vm.PreviewFrame);
    }

    [Fact]
    public async Task MalformedPixelsAndRecordingStateCannotPublish()
    {
        var client = new PreviewClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var poll = vm.PollPreviewAsync();
        client.Complete(0, [1, 2]);
        await poll;
        Assert.Null(vm.PreviewFrame);
        Assert.False(vm.StatusUnconfirmed);
        vm.ApplyReply(new(true, null, client.State with { Mode = ProjectMode.Recording }));
        client.Pending = new();
        await vm.PollPreviewAsync();
        Assert.False(client.Pending.Task.IsCompleted);
    }

    private sealed class PreviewClient : IProjectClient
    {
        public ProjectClip Clip = new() { Id = Guid.NewGuid(), Name = "Frame", InPts = 0, OutPts = 1000 };
        public ProjectSnapshot State = new(Guid.NewGuid(), "Test", "/test", 0, 0, ProjectMode.Ready, 1, false, false)
            { ServerInstanceId = Guid.NewGuid() };
        public TaskCompletionSource<ProjectReply> Pending = new();
        public void Complete(long ticks, byte[]? rgba = null) => Pending.SetResult(new(true, null, State) {
            Frame = new(0, ticks, Clip.Id, rgba ?? new byte[ProjectFrameReply.ByteCount]) });
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default) =>
            command == "GetProjectFrame" ? Pending.Task : Task.FromResult(new ProjectReply(true, null, State, [Clip]) {
                TimelineClips = [new(Clip.Id, 0, TimeSpan.TicksPerSecond)] });
    }
}
