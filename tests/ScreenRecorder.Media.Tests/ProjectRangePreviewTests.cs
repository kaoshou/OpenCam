// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
namespace ScreenRecorder.Media.Tests;
public sealed class ProjectRangePreviewTests
{
    [Fact]
    public async Task QualityChangeKeepsRangeAndDoesNotCancelItself()
    {
        var client=new RangeClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);await vm.PreviewRangeAsync();
        await vm.SetPreviewQualityAsync(ProjectPreviewQuality.P360);
        Assert.Equal(ProjectPreviewQuality.P360,vm.PreviewQuality);
        Assert.True(vm.IsPlayingPreview);Assert.Equal(3_000_000,client.EndTicks);
    }
    [Fact]
    public async Task ExplicitStopWhileRangeStartIsPendingPreventsLatePlayback()
    {
        var client=new RangeClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);
        client.HeldStop=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var start=vm.PreviewRangeAsync();var stop=vm.StopPreviewAsync();
        client.HeldStop.SetResult(new(true,null,client.State){Playback=new(Guid.Empty,0,false,null)});
        await Task.WhenAll(start,stop);
        Assert.DoesNotContain("play",client.Actions);Assert.False(vm.IsPlayingPreview);
    }
    [Fact]
    public async Task ManualSeekOutsideRangeCancelsBoundedLoop()
    {
        var client=new RangeClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);vm.RepeatPreviewRange=true;await vm.PreviewRangeAsync();
        vm.Seek(5_000_000);Assert.Null(client.EndTicks);
    }
    [Fact]
    public async Task RangePlaybackUsesBoundAndRepeatStopsBeforeRestartAndExplicitStopCancels()
    {
        var client=new RangeClient(); var vm=new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync(); Assert.False(vm.CanPreviewRange);
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000); vm.RepeatPreviewRange=true;
        await vm.PreviewRangeAsync();
        Assert.Equal(3_000_000,client.EndTicks); Assert.True(vm.IsPlayingPreview);
        client.Finished=true;await vm.PollPreviewAsync();
        Assert.Equal(new[]{"stop","play","stop","play"},client.Actions);
        await vm.StopPreviewAsync(); client.Finished=true;await vm.PollPreviewAsync();
        Assert.Equal(2,client.Actions.Count(a=>a=="play"));
    }
    [Fact]
    public async Task PlaybackErrorNeverRestartsOldLoop()
    {
        var client=new RangeClient(); var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);vm.RepeatPreviewRange=true;
        await vm.PreviewRangeAsync();client.Finished=true;client.Failure="decode failed";
        await vm.PollPreviewAsync();Assert.Equal(1,client.Actions.Count(a=>a=="play"));Assert.Equal("decode failed",vm.Error);
    }
    [Fact]
    public async Task DisablingRepeatDuringStopAcknowledgementDoesNotRestart()
    {
        var client=new RangeClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);vm.RepeatPreviewRange=true;await vm.PreviewRangeAsync();
        client.HeldStop=new(TaskCreationOptions.RunContinuationsAsynchronously);client.Finished=true;
        var poll=vm.PollPreviewAsync();Assert.False(poll.IsCompleted);
        vm.RepeatPreviewRange=false;
        client.HeldStop.SetResult(new(true,null,client.State){Playback=new(client.Generation,3_000_000,false,null)});
        await poll;Assert.Equal(1,client.Actions.Count(a=>a=="play"));
    }
    [Fact]
    public async Task RangeEditStopsCurrentRangeWithoutRestartingOldSelection()
    {
        var client=new RangeClient();var vm=new ProjectWorkspaceViewModel(client);await vm.RefreshAsync();
        vm.SetRangeStart(1_000_000);vm.SetRangeEnd(3_000_000);vm.RepeatPreviewRange=true;await vm.PreviewRangeAsync();
        vm.SetRangeEnd(4_000_000);
        Assert.False(vm.IsPlayingPreview);Assert.Equal(1,client.Actions.Count(a=>a=="play"));
    }
    private sealed class RangeClient:IProjectClient
    {
        public List<string> Actions=[]; public bool Finished;public string? Failure;public long? EndTicks;
        private Guid generation;private long ticks;
        public Guid Generation=>generation;
        public TaskCompletionSource<ProjectReply>? HeldStop;
        private readonly ProjectClip clip=new(){Id=Guid.NewGuid(),Name="clip",InPts=0,OutPts=1000};
        public ProjectSnapshot State=new(Guid.NewGuid(),"p","/p",0,0,ProjectMode.Ready,1,false,false){ServerInstanceId=Guid.NewGuid(),Canvas=new(64,36,new(30,1))};
        public Task<ProjectReply> SendAsync(string command,ProjectRequest request,CancellationToken ct=default)
        {
            if(command=="PlayProjectPreview"){Actions.Add("play");generation=Guid.NewGuid();ticks=request.TimelineTicks;EndTicks=request.PreviewEndTicks;Finished=false;}
            if(command=="StopProjectPreview")Actions.Add("stop");
            if(command=="StopProjectPreview"&&HeldStop is not null)return HeldStop.Task;
            if(command is "PlayProjectPreview" or "StopProjectPreview" or "GetProjectFrame")
                return Task.FromResult(new ProjectReply(true,null,State){Playback=new(generation,Finished?EndTicks??ticks:ticks,command!="StopProjectPreview"&&!Finished,Failure)});
            return Task.FromResult(new ProjectReply(true,null,State,[clip]){TimelineClips=[new(clip.Id,0,10_000_000)]});
        }
    }
}
