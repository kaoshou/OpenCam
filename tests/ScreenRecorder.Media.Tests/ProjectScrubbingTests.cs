// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectScrubbingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task TrimHandleCommitsOnceOrEscapeCancelsWithoutEditing(bool cancel)
    {
        var client=new ScrubClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SelectedClip=client.Clip;
        var timeline=new ProjectTimelineControl{DataContext=vm};var window=new Window{Content=timeline,Width=1000,Height=260};
        try{
            window.Show();window.UpdateLayout();
            window.MouseDown(new(980,75),MouseButton.Left);window.MouseMove(new(700,75));
            Assert.Empty(client.Edits);
            if(cancel)window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);
            window.MouseUp(new(700,75),MouseButton.Left);
            if(cancel)Assert.Empty(client.Edits);
            else {var edit=Assert.IsType<ProjectClipEdit.TrimEdge>(Assert.Single(client.Edits));Assert.False(edit.Start);Assert.True(edit.DeltaTicks<0);}
        }finally{window.Close();}
    }

    [Fact]
    public async Task ImmediateRefreshDrainsOnlyLatestPositionWithoutOverlappingRequests()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var signals = 0; vm.PreviewRequested += (_, _) => signals++;
        vm.Seek(1_000_000); var drain = vm.DrainPreviewRequestsAsync();
        vm.Seek(2_000_000); vm.Seek(6_000_000);
        await vm.DrainPreviewRequestsAsync();
        Assert.Equal(1_000_000, client.FrameTicks);
        var next = new TaskCompletionSource<ProjectReply>();
        var old = client.Pending; client.Pending = next;
        old.SetResult(new(true, null, client.State) { Frame = new(0, 1_000_000, client.Clip.Id, new byte[ProjectFrameReply.ByteCount]) });
        Assert.Equal(6_000_000, client.FrameTicks);
        client.Complete(); await drain;
        Assert.Equal(6_000_000, vm.PreviewFrame!.TimelineTicks);
        Assert.True(signals >= 3);
    }

    [Fact]
    public async Task ScrubUsesLightQualityAndSettledPositionRefetchesSameTicks()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.BeginPreviewScrub(); vm.Seek(2_000_000);
        var poll = vm.PollPreviewAsync(); client.Complete(); await poll;
        Assert.Equal(ProjectPreviewQuality.P360, client.FrameQuality);
        await vm.EndPreviewScrubAsync();
        client.Pending = new(); poll = vm.PollPreviewAsync();
        Assert.False(poll.IsCompleted);
        Assert.Equal(ProjectPreviewQuality.P720, client.FrameQuality);
        client.Complete(); await poll;
    }

    [Fact]
    public async Task FrameStillInFlightAtReleaseCannotReplaceFinalPositionOrNewProject()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var initial = vm.PollPreviewAsync(); client.Complete(); await initial;
        var picture = vm.PreviewFrame;
        vm.BeginPreviewScrub(); vm.Seek(2_000_000);
        client.Pending = new(); var poll = vm.PollPreviewAsync();
        vm.Seek(6_000_000); await vm.EndPreviewScrubAsync();
        client.Complete(); await poll;
        Assert.Same(picture, vm.PreviewFrame);
        client.Pending = new(); poll = vm.PollPreviewAsync(); client.Complete(); await poll;
        Assert.Equal(6_000_000, vm.PreviewFrame!.TimelineTicks);
        vm.BeginPreviewScrub(); vm.Seek(7_000_000);
        client.Pending = new(); poll = vm.PollPreviewAsync();
        vm.ApplyReply(new(true, null, client.State with { ProjectId = Guid.NewGuid() }));
        client.Complete(); await poll;
        Assert.Null(vm.PreviewFrame);
    }

    [Fact]
    public async Task DelayedStopCannotRewindDraggedPositionAndExplicitStopCancelsResume()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync(); await vm.TogglePlaybackAsync();
        client.HeldStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.BeginPreviewScrub(); vm.Seek(6_000_000);
        var end = vm.EndPreviewScrubAsync();
        Assert.False(end.IsCompleted);
        client.HeldStop.SetResult(new(true, null, client.State) { Playback = new(Guid.NewGuid(), 1_000_000, false, null) });
        await end;
        Assert.Equal(6_000_000, vm.PlayheadTicks);
        Assert.Equal(6_000_000, client.PlayTicks);
        client.HeldStop = null;
        vm.BeginPreviewScrub(); vm.Seek(7_000_000);
        await vm.StopPreviewAsync(); await vm.EndPreviewScrubAsync();
        Assert.False(vm.IsPlayingPreview);
        Assert.Equal(7_000_000, vm.PlayheadTicks);
    }
    [AvaloniaFact]
    public async Task DragKeepsLastPictureAndPublishesFramesBeforeRelease()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var initial = vm.PollPreviewAsync(); client.Complete(); await initial;
        var old = vm.PreviewFrame;
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var window = new Window { Content = timeline, Width = 1000, Height = 260 };
        try
        {
            window.Show(); window.UpdateLayout();
            window.MouseDown(new(200, 24), MouseButton.Left);
            window.MouseMove(new(300, 24));
            Assert.Same(old, vm.PreviewFrame);
            client.Pending = new();
            var poll = vm.PollPreviewAsync(); var requested = client.FrameTicks;
            window.MouseMove(new(500, 24));
            client.Complete(); await poll;
            Assert.Equal(requested, vm.PreviewFrame!.TimelineTicks);
            Assert.NotEqual(requested, vm.PlayheadTicks);
            // Releasing in the ruler must commit its latest pointer location, not the last move.
            window.MouseUp(new(600, 24), MouseButton.Left);
            Assert.InRange(vm.PlayheadTicks, 5_900_000, 6_200_000);
            client.Pending = new(); poll = vm.PollPreviewAsync(); client.Complete(); await poll;
            Assert.Equal(vm.PlayheadTicks, vm.PreviewFrame!.TimelineTicks);
            vm.ApplyReply(new(true, null, client.State with { Revision = 1 }));
            Assert.Null(vm.PreviewFrame);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PlayingDragStopsOnceAndResumesOnlyAtReleasePosition()
    {
        var client = new ScrubClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync(); await vm.TogglePlaybackAsync();
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var window = new Window { Content = timeline, Width = 1000, Height = 260 };
        try
        {
            window.Show(); window.UpdateLayout();
            window.MouseDown(new(200, 24), MouseButton.Left);
            for (var x = 250; x < 550; x += 25) window.MouseMove(new(x, 24));
            Assert.False(vm.IsPlayingPreview);
            Assert.Equal(1, client.Plays);
            Assert.Equal(1, client.Stops);
            window.MouseUp(new(600, 24), MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsPlayingPreview);
            Assert.Equal(2, client.Plays);
            Assert.Equal(vm.PlayheadTicks, client.PlayTicks);
            Assert.InRange(client.PlayTicks, 5_900_000, 6_200_000);
        }
        finally { window.Close(); }
    }

    private sealed class ScrubClient : IProjectClient
    {
        public ProjectClip Clip = new() { Id = Guid.NewGuid(), Name = "Frame", InPts = 0, OutPts = 1000 };
        public ProjectSnapshot State = new(Guid.NewGuid(), "Test", "/test", 0, 0, ProjectMode.Ready, 1, false, false)
            { ServerInstanceId = Guid.NewGuid() };
        public TaskCompletionSource<ProjectReply> Pending = new();
        public long FrameTicks, PlayTicks;
        public ProjectPreviewQuality FrameQuality;
        public int Plays, Stops;
        public List<ProjectClipEdit> Edits=[];
        public TaskCompletionSource<ProjectReply>? HeldStop;
        private readonly Guid generation = Guid.NewGuid();
        public void Complete() => Pending.SetResult(new(true, null, State) {
            Frame = new(0, FrameTicks, Clip.Id, new byte[ProjectFrameReply.ByteCount]) });
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if(command=="ApplyProjectEdit" && request.Edit is { } edit)Edits.Add(edit);
            if (command == "GetProjectFrame") { FrameTicks = request.TimelineTicks; FrameQuality = request.PreviewQuality; return Pending.Task; }
            if (command == "PlayProjectPreview") { Plays++; PlayTicks = request.TimelineTicks;
                return Task.FromResult(new ProjectReply(true, null, State) { Playback = new(generation, PlayTicks, true, null) }); }
            if (command == "StopProjectPreview") { Stops++;
                if (HeldStop is not null) return HeldStop.Task;
                return Task.FromResult(new ProjectReply(true, null, State) { Playback = new(generation, PlayTicks, false, null) }); }
            return Task.FromResult(new ProjectReply(true, null, State, [Clip]) {
                TimelineClips = [new(Clip.Id, 0, TimeSpan.TicksPerSecond)] });
        }
    }
}
