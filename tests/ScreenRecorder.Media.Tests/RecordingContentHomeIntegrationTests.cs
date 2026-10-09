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
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ClosingEditorPromptsButReturningHomeKeepsEdits()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        var vm = await main.PrepareProjectWorkspaceAsync();
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "saved");
        var window = new ProjectWorkspaceView(vm, main);
        window.Show();
        try
        {
            await vm.RenameProjectAsync("pending");
            vm.RequestUnsavedDecision = () => Task.FromResult(UnsavedDecision.Cancel);
            Assert.False(await window.RequestCloseAsync());
            Assert.True(window.IsVisible);
            Assert.True(vm.State.IsDirty);
            vm.RequestUnsavedDecision = () => Task.FromResult(UnsavedDecision.Save);
            Assert.True(await window.RequestCloseAsync());
            Assert.False(vm.State.IsDirty);
            Assert.Equal("pending", coordinator.Current!.Name);
        }
        finally { vm.RequestUnsavedDecision = () => Task.FromResult(UnsavedDecision.Discard); await window.RequestCloseAsync(); main.Cleanup(); }
    }

    [Fact]
    public async Task ReturnHomeKeepsWorkingEdits()
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
            await vm.CreateAsync(scope.Configuration.OutputDirectory, "saved");
            var path = Path.Combine(coordinator.ProjectDirectory!, "project.opencam");
            var before = await File.ReadAllBytesAsync(path);
            await vm.RenameProjectAsync("pending rename");
            vm.RequestUnsavedDecision = () => throw new InvalidOperationException("Returning home must not prompt or discard edits.");
            Assert.True(await main.LeaveContentEditorAsync());
            Assert.True(main.CanOpenContentEditor);
            Assert.Same(vm, await main.PrepareProjectWorkspaceAsync());
            Assert.Equal("pending rename", vm.State.Name);
            Assert.True(vm.State.IsDirty);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally { main.Cleanup(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordingStatusPollingDoesNotShowStartupBannerButStillBlocksOnLostStatus(bool loseStatus)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdStatus = false;
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient(async (command, request, ct) =>
        {
            if (holdStatus && command == "GetProjectStatus")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                if (loseStatus) throw new IOException("Status connection lost");
            }
            return await dispatcher.DispatchAsync(new()
                { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
        }), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = name => Task.FromResult<string?>(name);
        try
        {
            var editor = await main.PrepareProjectWorkspaceAsync();
            await main.StartRecordingAsync();
            Assert.True(main.IsRecording);
            Assert.False(main.IsPreparing);
            var preparingTransitions = new List<bool>();
            main.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(main.IsPreparing)) preparingTransitions.Add(main.IsPreparing);
            };
            holdStatus = true;
            var poll = editor.PollRecordingAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                Assert.False(main.IsPreparing);
                Assert.Equal(main.Strings["StatusRecordingActive"], main.StatusMessage);
                Assert.True(main.CanStopRecording);
                Assert.False(main.CanOpenContentEditor);
                Assert.False(main.CanManageRecordingProject);
            }
            finally { release.TrySetResult(); await poll; }
            if (loseStatus)
            {
                Assert.True(editor.StatusUnconfirmed);
                Assert.True(main.IsPreparing);
                Assert.True(main.IsApplicationCloseBlocked);
                Assert.False(string.IsNullOrWhiteSpace(editor.Error));
                Assert.Equal(editor.Error, main.StatusMessage);
            }
            else
            {
                Assert.DoesNotContain(true, preparingTransitions);
                Assert.True(main.IsRecording);
                Assert.Single(scope.Factory.Paths);
            }
        }
        finally { release.TrySetResult(); main.Cleanup(); }
    }

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
