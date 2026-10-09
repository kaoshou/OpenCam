// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPreviewPresenterTests
{
    [Fact]
    public async Task SelectingLateClipLoadsItsThumbnailWithoutGrowingMemoryOrMovingPlayhead()
    {
        var client = new ManyClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        for (var i = 0; i < 64; i++) await vm.PollThumbnailAsync();
        Assert.Equal(64, vm.Thumbnails.Count);
        vm.SelectedClip = vm.Clips[90];
        await vm.PollThumbnailAsync();
        Assert.Contains(vm.Clips[90].Id, vm.Thumbnails.Keys);
        Assert.InRange(vm.Thumbnails.Count, 1, 64);
        Assert.Equal(0, vm.PlayheadTicks);
    }

    private sealed class ManyClipClient : IProjectClient
    {
        private readonly ProjectClip[] clips = Enumerable.Range(0, 100).Select(i => new ProjectClip {
            Id = Guid.NewGuid(), Name = $"Clip {i}", InPts = 0, OutPts = 1000 }).ToArray();
        private readonly ProjectSnapshot state = new(Guid.NewGuid(), "Long project", "/test", 0, 0,
            ProjectMode.Ready, 100, false, false) { ServerInstanceId = Guid.NewGuid() };
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            var timeline = clips.Select((clip, i) => new ProjectTimelineClip(clip.Id,
                i * TimeSpan.TicksPerSecond, (i + 1) * TimeSpan.TicksPerSecond)).ToArray();
            return Task.FromResult(command == "GetProjectFrame"
                ? new ProjectReply(true, null, state) { Frame = new(0, request.TimelineTicks,
                    clips[(int)(request.TimelineTicks / TimeSpan.TicksPerSecond)].Id, new byte[ProjectFrameReply.ByteCount]) }
                : new ProjectReply(true, null, state, clips) { TimelineClips = timeline });
        }
    }

    [Fact]
    public async Task AudiblePlaybackDoesNotChangeInspectorSelectionAndPauseKeepsPosition()
    {
        var client = new PreviewClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedClip = client.Clip;
        await vm.TogglePlaybackAsync();
        Assert.True(vm.IsPlayingPreview);
        var poll = vm.PollPreviewAsync();
        client.Pending.SetResult(new(true, null, client.State) {
            Playback = new(client.Generation, 500000, true, null),
            Frame = new(0, 333333, client.Clip.Id, new byte[ProjectFrameReply.ByteCount]) });
        await poll;
        Assert.Equal(500000, vm.PlayheadTicks);
        Assert.Same(client.Clip, vm.SelectedClip);
        vm.ClipVolumePercent = 50;
        Assert.True(vm.CanPreview); // Editing a draft must never disable Pause.
        await vm.TogglePlaybackAsync();
        Assert.False(vm.IsPlayingPreview);
        Assert.Equal(500000, vm.PlayheadTicks);
    }
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
        public Guid Generation = Guid.NewGuid();
        public void Complete(long ticks, byte[]? rgba = null) => Pending.SetResult(new(true, null, State) {
            Frame = new(0, ticks, Clip.Id, rgba ?? new byte[ProjectFrameReply.ByteCount]) });
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default) =>
            command == "PlayProjectPreview" ? Task.FromResult(new ProjectReply(true, null, State) { Playback = new(Generation, request.TimelineTicks, true, null) }) :
            command == "StopProjectPreview" ? Task.FromResult(new ProjectReply(true, null, State) { Playback = new(Generation, 500000, false, null) }) :
            command == "GetProjectFrame" ? Pending.Task : Task.FromResult(new ProjectReply(true, null, State, [Clip]) {
                TimelineClips = [new(Clip.Id, 0, TimeSpan.TicksPerSecond)] });
    }
}
