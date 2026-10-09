// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task HomeKeepsFinalizationCloseBlockedAndOffersExportCancellation()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        try
        {
            var vm = await main.PrepareProjectWorkspaceAsync();
            vm.ApplyReply(new(true, null, new(Guid.NewGuid(), "Saving", null, 1, 1, ProjectMode.SavingSegment, 1, false, false)));
            Assert.True(main.IsPreparing);
            Assert.True(main.IsApplicationCloseBlocked);
            vm.ApplyReply(new(true, null, vm.State with { Mode = ProjectMode.Ready,
                Export = new(Guid.NewGuid(), 1, RecordingExportState.Running, .2) }));
            Assert.True(main.IsContentExporting);
            Assert.True(main.CancelContentExportCommand.CanExecute(null));
            vm.ApplyReply(new(true, null, vm.State with { Export = null, ClipCount = 0 }));
            Assert.True(main.CanOpenContentEditor); // Empty timelines must remain reopenable for undo / resume.
            vm.StatusUnconfirmed = true;
            Assert.False(main.CanForceQuitUnconfirmed);
            for (var i = 0; i < 6; i++) main.NoteContentConnectionFailure();
            Assert.True(main.CanForceQuitUnconfirmed);
            Assert.True(main.IsApplicationCloseBlocked);
            Assert.True(main.TryAuthorizeForceQuit());
            Assert.False(main.IsApplicationCloseBlocked);
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task HomeInsertionUsesSameOwnerAndCanReturnFromPausedEditor()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = name => Task.FromResult<string?>(name);
        try
        {
            await main.StartRecordingAsync();
            await main.TogglePauseResumeAsync();
            var vm = await main.PrepareProjectWorkspaceAsync();
            Assert.True(await main.LeaveContentEditorAsync());
            Assert.Equal(ProjectMode.Paused, vm.State.Mode);
            await main.StartEditorRecordingAsync(TimeSpan.TicksPerSecond / 2, vm.State.Revision);
            Assert.True(main.IsRecording);
            await main.TogglePauseResumeAsync();
            Assert.Equal(3, vm.Clips.Count);
            await vm.UndoAsync();
            Assert.Single(vm.Clips);
            await vm.FinishAsync();
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task HomeAndEditorShareCapturePauseResumeAndStopWithoutCreatingAProjectManually()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var client = new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(client, () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = name => Task.FromResult<string?>(name);
        try
        {
            await main.StartRecordingAsync();
            Assert.True(main.IsRecording);
            await main.TogglePauseResumeAsync();
            var editor = await main.PrepareProjectWorkspaceAsync();
            Assert.Equal(ProjectMode.Paused, editor.State.Mode);
            Assert.True(main.CanStopRecording); // An open editor must not disable home controls.
            await main.StartEditorRecordingAsync();
            Assert.True(main.IsRecording);
            Assert.Equal(ProjectMode.Recording, editor.State.Mode);
            await main.StopRecordingAsync();
            await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(2, export.Snapshot!.Clips.Length);
            export.Complete.SetResult(new(false, null, "Retained test sources"));
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task EditorPermissionFailureIsVisibleInEditorAndDoesNotStartCapture()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => false, () => Task.CompletedTask);
        try
        {
            var editor = await main.PrepareProjectWorkspaceAsync();
            await editor.CreateAsync(scope.Configuration.OutputDirectory, "Permission test");
            await main.StartEditorRecordingAsync();
            Assert.Equal(main.Strings["StatusScreenPermissionRequired"], editor.Error);
            Assert.Empty(scope.Factory.Paths);
        }
        finally { main.Cleanup(); }
    }
}
