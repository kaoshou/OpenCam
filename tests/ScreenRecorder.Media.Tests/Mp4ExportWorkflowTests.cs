// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class Mp4ExportWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mp4AsksForOptionsBeforeDestinationAndDispatchesCapturedScope(bool clip)
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var events = new List<string>();
        var chosen = new Mp4ExportOptions(Mp4ExportQuality.Compact, Mp4ExportResolution.Hd);
        var id = clip ? client.Clips[1].Id : (Guid?)null;
        Func<ProjectExportTarget, Task<Mp4ExportOptions?>> options = target => {
            events.Add("options"); Assert.Equal(id, target.ClipId); return Task.FromResult<Mp4ExportOptions?>(chosen); };
        Func<ProjectExportTarget, ProjectExportFormat, Task<string?>> destination = (_, _) => {
            events.Add("destination"); return Task.FromResult<string?>("/test/compressed.mp4"); };
        await RequestMp4(vm, id, options, destination);
        Assert.Equal(new[] { "options", "destination" }, events);
        var request = Assert.Single(client.Exports);
        Assert.Equal(chosen, request.ExportOptions!.Mp4);
        Assert.Equal(clip ? ProjectExportScope.Clip : ProjectExportScope.WholeProject, request.ExportOptions.Scope);
        Assert.Equal(id, request.ExportOptions.ClipId);
        Assert.Equal(7, request.ExpectedRevision);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("server")]
    [InlineData("deleted")]
    [InlineData("unknown")]
    public async Task CancelOrChangedTargetAfterOptionsDoesNotOpenDestinationOrExport(string change)
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        await RequestMp4(vm, client.Clips[1].Id, _ => {
            if (change == "revision") vm.State = vm.State with { Revision = 8, SavedRevision = 8 };
            if (change == "project") vm.State = vm.State with { ProjectId = Guid.NewGuid() };
            if (change == "server") vm.State = vm.State with { ServerInstanceId = Guid.NewGuid() };
            if (change == "deleted") vm.Clips.RemoveAt(1);
            if (change == "unknown") vm.StatusUnconfirmed = true;
            return Task.FromResult<Mp4ExportOptions?>(change == "cancel" ? null : new());
        }, (_, _) => throw new Exception("Stale/canceled selection must not open destination"));
        Assert.Empty(client.Exports);
    }

    [Fact]
    public async Task WebPlayerUsesSameProfileAndAsksBeforeParentFolder()
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var ordered = new List<string>();
        var profile = new Mp4ExportOptions(Mp4ExportQuality.Standard, Mp4ExportResolution.FullHd);
        Func<ProjectExportTarget, Task<Mp4ExportOptions?>> options = _ => {
            ordered.Add("options"); return Task.FromResult<Mp4ExportOptions?>(profile); };
        Func<Task<string?>> destination = () => { ordered.Add("destination"); return Task.FromResult<string?>("/test"); };
        var method = typeof(ProjectWorkspaceViewModel).GetMethod("RequestWebExportAsync");
        Assert.NotNull(method);
        await (Task)method.Invoke(vm, [options, destination])!;
        Assert.Equal(new[] { "options", "destination" }, ordered);
        var request = Assert.Single(client.Exports);
        Assert.True(request.ExportWebPlayer);
        Assert.Equal(profile, request.ExportOptions!.Mp4);
        Assert.Equal(ProjectExportScope.WholeProject, request.ExportOptions.Scope);
    }

    private static async Task RequestMp4(ProjectWorkspaceViewModel vm, Guid? clip,
        Func<ProjectExportTarget, Task<Mp4ExportOptions?>> options,
        Func<ProjectExportTarget, ProjectExportFormat, Task<string?>> destination)
    {
        var method = typeof(ProjectWorkspaceViewModel).GetMethods().SingleOrDefault(m =>
            m.Name == "RequestExportAsync" && m.GetParameters().Length == 5);
        Assert.NotNull(method);
        Func<ProjectExportTarget, Task<GifExportOptions?>> gif = _ => throw new Exception("Not a GIF export");
        await (Task)method.Invoke(vm, [ProjectExportFormat.Mp4, clip, gif, destination, options])!;
    }
}
