// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
namespace ScreenRecorder.Media.Tests;
public sealed class ProjectEditorStatusTests
{
    [Fact]
    public async Task BackgroundStatusCannotEraseFirstPreviewFailure()
    {
        var state=new ProjectSnapshot(Guid.NewGuid(),"p","/p",1,0,ProjectMode.Ready,0,false,false);
        var vm=new ProjectWorkspaceViewModel(new StatusClient(state));vm.ApplyReply(new(true,null,state));
        vm.Error="first preview failure";await vm.PollRecordingAsync();
        Assert.Equal("first preview failure",vm.Error);
    }
    [Fact]
    public void PreviewFailureDoesNotFalselyReportProjectSaveFailure()
    {
        var vm=new ProjectWorkspaceViewModel(new StatusClient());
        vm.ApplyReply(new(true,null,new(Guid.NewGuid(),"p","/p",0,0,ProjectMode.Ready,0,false,false)));
        vm.Error="preview decode failure";
        Assert.Equal(vm.Strings["ProjectSaved"],vm.SaveStatus);
    }
    [Fact]
    public void OldRevisionOrDifferentOwnerNeverReportsCurrentExport()
    {
        var vm=new ProjectWorkspaceViewModel(new StatusClient());
        var export=new RecordingExportStatus(Guid.NewGuid(),2,RecordingExportState.Succeeded,1,"/output.mp4");
        var state=new ProjectSnapshot(Guid.NewGuid(),"p","/p",2,2,ProjectMode.Ready,1,false,false){ServerInstanceId=Guid.NewGuid(),Export=export};
        vm.ApplyReply(new(true,null,state));
        var current=vm.CurrentExportStatusText;
        vm.ApplyReply(new(true,null,state with { Revision=3 }));
        Assert.NotEqual(current,vm.CurrentExportStatusText);
        vm.ApplyReply(new(true,null,state with { ProjectId=Guid.NewGuid(),ServerInstanceId=Guid.NewGuid() }));
        Assert.NotEqual(current,vm.CurrentExportStatusText);
    }
    private sealed class StatusClient(ProjectSnapshot? snapshot=null):IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command,ProjectRequest request,CancellationToken ct=default)=>Task.FromResult(new ProjectReply(true,null,snapshot??ProjectSnapshot.Closed));
    }
}
