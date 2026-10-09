// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectManualExport_SavesPendingPropertiesAndExportsPausedSnapshotWithoutResuming()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Manual export");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        var paths = scope.Factory.Paths.Count;
        vm.ClipName = "saved before export";
        Assert.True(vm.CanExport);
        await vm.ExportAsync(scope.Configuration.OutputDirectory);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("saved before export", export.Snapshot!.Clips[0].Name);
        Assert.Equal(coordinator.SavedRevision, export.Snapshot.Revision);
        Assert.Equal(ProjectMode.Paused, coordinator.Mode);
        Assert.Equal(paths, scope.Factory.Paths.Count);
        Assert.True(vm.IsExporting);
        Assert.False(vm.CanExport);
        Assert.False(vm.CanRecord);
        await vm.CancelExportAsync();
        await UntilAsync(() => coordinator.ExportStatus?.State == RecordingExportState.Canceled);
        await vm.RefreshAsync();
        Assert.True(vm.CanExport);
        Assert.True(vm.CanRecord);
        await vm.FinishAsync();
    }

    [Fact]
    public async Task ProjectManualExport_ReopenedContentAcceptsChosenDestinationAndRejectsStaleRevision()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Reopen")).Success);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        var manifest = Path.Combine(coordinator.ProjectDirectory!, "project.opencam");
        Assert.True((await coordinator.CloseAsync()).Success);
        Assert.True((await coordinator.OpenAsync(manifest)).Success);
        Assert.Null(coordinator.OutputDirectory);
        var revision = coordinator.Current!.Revision;
        Assert.False((await coordinator.ExportAsync(scope.Configuration.OutputDirectory, revision - 1, Guid.NewGuid())).Success);
        Assert.Equal(0, export.Calls);
        Assert.True((await coordinator.ExportAsync(scope.Configuration.OutputDirectory, revision, Guid.NewGuid())).Success);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(scope.Configuration.OutputDirectory, export.Destination);
        await coordinator.CancelExportAsync(coordinator.ExportStatus!.ExportId);
    }
}
