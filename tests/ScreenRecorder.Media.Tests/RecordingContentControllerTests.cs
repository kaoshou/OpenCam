// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task RecordingContentController_StartStopNeedsNoEditorAndAutomaticallyQueuesSavedOutput()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var controller = new RecordingContentController(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), _ => Task.CompletedTask);
        await controller.StartNewAsync(scope.Configuration);
        Assert.Equal(ProjectMode.Recording, controller.Workspace.State.Mode);
        Assert.False(controller.CanOpenEditor);
        Assert.NotNull(controller.Workspace.State.ProjectId);
        Assert.Single(scope.Factory.Paths);
        await controller.StopAsync();
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(export.Snapshot!.Clips);
        Assert.Equal(coordinator.SavedRevision, export.Snapshot.Revision);
        Assert.False(controller.CanStartNew);
        Assert.False(controller.CanOpenEditor);
        export.Complete.SetResult(new(false, null, "Export error; original recording retained"));
        await UntilAsync(() => coordinator.ExportStatus?.State == RecordingExportState.Failed);
        await controller.RefreshAsync();
        Assert.True(controller.CanOpenEditor);
        Assert.True(controller.CanStartNew);
        Assert.Single(controller.Workspace.Clips);
        Assert.True(File.Exists(scope.Factory.Paths[0]));
    }

    [Fact]
    public async Task RecordingContentController_ResumeWaitsForPreviewStopThenSavesEditorDraft()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        Task stop = Task.CompletedTask;
        var controller = new RecordingContentController(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), _ => stop);
        await controller.StartNewAsync(scope.Configuration);
        await controller.PauseAsync();
        Assert.True(controller.CanOpenEditor);
        controller.Workspace.ClipName = "Edited from shared editor";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stop = release.Task;
        var resume = controller.ResumeAsync(scope.Configuration);
        Assert.True(controller.IsBusy);
        Assert.False(controller.CanOpenEditor);
        Assert.Single(scope.Factory.Paths);
        await controller.ResumeAsync(scope.Configuration); // A second click cannot queue a second capture.
        release.SetResult();
        await resume;
        Assert.Equal(2, scope.Factory.Paths.Count);
        Assert.Equal("Edited from shared editor", coordinator.Current!.Clips[0].Name);
        Assert.Equal(coordinator.Current.Revision, coordinator.SavedRevision);
        Assert.Equal(ProjectMode.Recording, controller.Workspace.State.Mode);
        await controller.Workspace.FinishAsync();
    }

    [Fact]
    public async Task RecordingContentController_PreviewStopFailureDoesNotCaptureOrClearExistingContent()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var controller = new RecordingContentController(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })),
            _ => Task.FromException(new IOException("Preview stop not acknowledged")));
        await controller.StartNewAsync(scope.Configuration);
        Assert.Empty(scope.Factory.Paths);
        Assert.Null(coordinator.Current);
        Assert.Equal("Preview stop not acknowledged", controller.Workspace.Error);
    }
}
