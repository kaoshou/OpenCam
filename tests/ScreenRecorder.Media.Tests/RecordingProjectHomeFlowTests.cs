// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task EditorPreparationRechecksRecordingStateAfterAwaitingConnection()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        var connection = new TaskCompletionSource();
        var calls = 0;
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true,
            () => ++calls == 2 ? connection.Task : Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Race");
        try
        {
            await main.NewRecordingProjectAsync();
            var opening = main.PrepareProjectWorkspaceAsync();
            Assert.False(opening.IsCompleted);
            await main.StartRecordingAsync();
            Assert.True(main.IsRecording);
            connection.SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => opening);
            Assert.False(main.IsProjectWorkspaceOpen);
            Assert.Equal(ProjectMode.Recording, coordinator.Mode);
        }
        finally { connection.TrySetResult(); await coordinator.FinishAsync(Guid.NewGuid()); main.Cleanup(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordingBlocksEditorReentryAndProjectReplacementWithoutInterruptingCapture(bool returnBeforeResume)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        var home = new ScreenRecorder.UI.Views.MainWindow { DataContext = main };
        ProjectWorkspaceView? editor = null;
        home.Show();
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Capture guard");
        try
        {
            await main.StartRecordingAsync();
            await main.TogglePauseResumeAsync();
            home.UpdateLayout();
            var edit = home.GetVisualDescendants().OfType<ScreenRecorder.UI.Projects.Editor.EditorToolContent>()
                .Single(c => c.Text == main.Strings["ContentEditor"]).GetVisualAncestors().OfType<Button>().First();
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor = Assert.Single(home.OwnedWindows.OfType<ProjectWorkspaceView>());
            if (returnBeforeResume) await editor.ReturnToRecordingAsync();
            await main.TogglePauseResumeAsync();
            Assert.False(editor.IsVisible);
            var projectId = coordinator.Current!.ProjectId;
            Assert.True(main.IsRecording);
            Assert.False(main.CanOpenContentEditor);
            Assert.False(main.CanManageRecordingProject);
            // Even a programmatic invocation must not take the existing-window shortcut.
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(editor.IsVisible);
            await main.NewRecordingProjectAsync();
            await main.OpenRecordingProjectAsync(Path.Combine(scope.Configuration.OutputDirectory, "missing.opencam"));
            Assert.Equal(projectId, coordinator.Current.ProjectId);
            Assert.Equal(ProjectMode.Recording, coordinator.Mode);
            Assert.Equal(2, scope.Factory.Paths.Count);
        }
        finally
        {
            if (main.IsRecording) await main.TogglePauseResumeAsync();
            if (editor?.DataContext is ProjectWorkspaceViewModel vm) await vm.FinishAsync();
            if (editor is not null) await editor.RequestCloseAsync();
            home.Close(); main.Cleanup();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HomeBackupRecoveryCanBeConfirmedOrLeftWithoutTrappingUser(bool recover)
    {
        await using var scope = new RecordingScope();
        var store = new JsonProjectStore();
        string path;
        await using (var created = await store.CreateAsync(scope.Configuration.OutputDirectory, "Backup"))
        {
            path = Path.Combine(created.ProjectDirectory, "project.opencam");
            await created.SaveAsync(created.Current with { Revision = 1, Name = "Latest" }, 0);
        }
        await File.WriteAllTextAsync(path, "broken");
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, store, new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Other");
        try
        {
            await main.OpenRecordingProjectAsync(path);
            Assert.False(main.CanStartRecording);
            Assert.True(main.CanOpenContentEditor);
            var editor = await main.PrepareProjectWorkspaceAsync();
            Assert.True(editor.State.NeedsRecoveryConfirmation);
            if (recover)
            {
                await editor.RestoreBackupAsync();
                Assert.True(main.CanStartRecording);
                Assert.Equal("Backup", main.CurrentRecordingProjectName);
            }
            else
            {
                Assert.True(await main.LeaveContentEditorAsync());
                Assert.True(main.CanManageRecordingProject);
                await main.NewRecordingProjectAsync();
                Assert.Equal("Other", main.CurrentRecordingProjectName);
                Assert.Equal("broken", await File.ReadAllTextAsync(path));
            }
        }
        finally { main.Cleanup(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ActualHomeDialogCancelAndEditorChromeUseTheApprovedFlow()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        var home = new ScreenRecorder.UI.Views.MainWindow { DataContext = main };
        ProjectWorkspaceView? editor = null;
        home.Show();
        try
        {
            var start = main.StartRecordingAsync();
            var dialog = Assert.Single(home.OwnedWindows);
            dialog.UpdateLayout();
            Assert.False(string.IsNullOrWhiteSpace(dialog.GetVisualDescendants().OfType<TextBox>().Single().Text));
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, main.Strings["Cancel"]))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await start;
            Assert.Null(coordinator.Current);
            Assert.Empty(scope.Factory.Paths);
            var create = main.NewRecordingProjectAsync();
            dialog = Assert.Single(home.OwnedWindows);
            dialog.UpdateLayout();
            dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "UI lesson";
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, main.Strings["Confirm"]))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await create;
            Assert.Equal("UI lesson", main.CurrentRecordingProjectName);
            Assert.Empty(scope.Factory.Paths);
            var vm = await main.PrepareProjectWorkspaceAsync();
            editor = new(vm, main);
            editor.ShowForRecordingWindow(home); editor.UpdateLayout();
            var contents = editor.GetVisualDescendants().OfType<Button>().Select(b => b.Content).ToArray();
            foreach (var key in new[] { "ProjectNew", "ProjectOpen", "ProjectPause", "ProjectFinish", "ProjectContinue" })
                Assert.DoesNotContain(main.Strings[key], contents);
            Assert.Contains(main.Strings["ProjectSave"], contents);
            Assert.Single(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.SaveStatus);
            await editor.ReturnToRecordingAsync();
            Assert.False(editor.IsVisible);
            Assert.Equal("UI lesson", coordinator.Current!.Name);
            Assert.True(home.IsVisible);
        }
        finally { if (editor is not null) await editor.RequestCloseAsync(); home.Close(); main.Cleanup(); }
    }

    [Fact]
    public async Task HomeNameConfirmationCancelAndDuplicateClickCannotStartRecording()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        try
        {
            var answer = new TaskCompletionSource<string?>();
            main.RequestRecordingProjectName = proposed => {
                Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.\d{2}$", proposed);
                return answer.Task;
            };
            var start = main.StartRecordingAsync();
            Assert.True(main.IsPreparing);
            Assert.Null(coordinator.Current);
            await main.StartRecordingAsync();
            Assert.Empty(scope.Factory.Paths);
            answer.SetResult(null);
            await start;
            Assert.False(main.IsPreparing);
            Assert.Equal(main.Strings["StatusReady"], main.StatusMessage);
            Assert.Null(coordinator.Current);
            main.RequestRecordingProjectName = _ => Task.FromResult<string?>("My confirmed lesson");
            await main.StartRecordingAsync();
            Assert.True(main.IsRecording);
            Assert.Equal("My confirmed lesson", coordinator.Current!.Name);
            Assert.Single(scope.Factory.Paths);
            await main.TogglePauseResumeAsync();
            var editor = await main.PrepareProjectWorkspaceAsync();
            await editor.FinishAsync();
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task HomeCreateDoesNotCaptureAndReopenedProjectAppendsAfterStop()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        try
        {
            main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Existing lesson");
            await main.NewRecordingProjectAsync();
            Assert.Empty(scope.Factory.Paths);
            var model = await main.PrepareProjectWorkspaceAsync();
            Assert.Equal("Existing lesson", main.CurrentRecordingProjectName);
            Assert.Equal(main.Strings["StatusReady"], main.StatusMessage);
            var id = coordinator.Current!.ProjectId;
            var path = Path.Combine(coordinator.ProjectDirectory!, "project.opencam");
            await main.StartRecordingAsync();
            await main.StopRecordingAsync();
            await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            export.Complete.TrySetResult(new(false, null, "Synthetic output unavailable; retain project"));
            await UntilAsync(() => coordinator.ExportStatus?.State == RecordingExportState.Failed);
            // The same polling used by the home timer must restore editor access after finalization.
            // Refresh through the shared model; no manual project creation in the editor.
            await model.RefreshAsync();
            Assert.True(main.CanOpenContentEditor);
            Assert.Single((await main.PrepareProjectWorkspaceAsync()).Clips);
            await main.NewRecordingProjectAsync();
            Assert.NotEqual(id, coordinator.Current!.ProjectId);
            await main.OpenRecordingProjectAsync(path);
            Assert.Equal(id, coordinator.Current!.ProjectId);
            await main.StartRecordingAsync();
            await main.TogglePauseResumeAsync();
            Assert.Equal(2, (await main.PrepareProjectWorkspaceAsync()).Clips.Count);
            await model.FinishAsync();
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task HomeStartAppendsToCurrentProjectInsteadOfReplacingIt()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        try
        {
            var editor = await main.PrepareProjectWorkspaceAsync();
            await editor.CreateAsync(scope.Configuration.OutputDirectory, "Keep my project");
            var id = editor.State.ProjectId;
            await main.StartRecordingAsync();
            Assert.Equal(id, coordinator.Current!.ProjectId);
            await editor.FinishAsync();
            await main.StartRecordingAsync();
            await editor.FinishAsync();
            Assert.Equal(id, coordinator.Current.ProjectId);
            Assert.Equal(2, coordinator.Current.Clips.Length);
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public void ClosedEditorDoesNotClaimProjectWasSaved()
    {
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((_, _, _) => throw new InvalidOperationException()));
        Assert.Equal("", vm.SaveStatus);
    }
}
