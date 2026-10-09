using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":\"broken\"}")]
    public async Task CorruptRecentProjectDoesNotPreventEditorOpening(string malformed)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        var main = new MainViewModel(forScreenshot: true);
        await using var valid = await new JsonProjectStore().CreateAsync(scope.Configuration.OutputDirectory, "Valid");
        var bad = Path.Combine(scope.Configuration.OutputDirectory, "project.opencam");
        await File.WriteAllTextAsync(bad, malformed);
        main.RecentProjectPaths.Clear();
        main.RecentProjectPaths.Add(bad);
        main.RecentProjectPaths.Add(Path.Combine(valid.ProjectDirectory, "project.opencam"));
        var editor = new ProjectWorkspaceView(vm, main);
        try
        {
            var refresh = typeof(ProjectWorkspaceView).GetMethod("RefreshRecentProjectsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)refresh.Invoke(editor, null)!;
            var combo = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.ComboBox>(editor, "RecentProjects")!;
            Assert.Equal(2, combo.Items.Count);
            Assert.Contains("Valid", combo.Items[1]!.ToString());
        }
        finally { await editor.RequestCloseAsync(); main.Cleanup(); }
    }

    [Fact]
    public async Task NamedRecordingCanRenameUndoSaveAndReopenWithoutMovingSources()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        scope.Configuration.ProjectName = "教學 / 第一課";
        var result = await coordinator.StartNewContentAsync(scope.Configuration, Guid.NewGuid());
        Assert.True(result.Success);
        await coordinator.PauseAsync(Guid.NewGuid());
        await vm.RefreshAsync();
        var root = coordinator.ProjectDirectory!;
        Assert.Contains("教學 - 第一課", Path.GetFileName(root));
        var clips = coordinator.Current!.Clips;
        await vm.RenameProjectAsync("新版教學");
        Assert.Null(vm.Error);
        Assert.Equal("新版教學", coordinator.Current!.Name);
        Assert.Equal(root, coordinator.ProjectDirectory);
        Assert.Equal(clips, coordinator.Current.Clips);
        await vm.UndoAsync();
        Assert.Equal("教學 / 第一課", coordinator.Current.Name);
        await vm.RedoAsync();
        await vm.FinishAsync();
        Assert.True(await vm.CloseAsync());
        await vm.OpenAsync(Path.Combine(root, "project.opencam"));
        Assert.Equal("新版教學", vm.State.Name);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ReturnToRecordingWorksAfterRepeatedReopening()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        var main = new MainViewModel(forScreenshot: true);
        var home = new Avalonia.Controls.Window();
        var editor = new ProjectWorkspaceView(vm, main);
        home.Show();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                editor.ShowForRecordingWindow(home);
                Assert.True(editor.IsVisible);
                await editor.ReturnToRecordingAsync();
                Assert.False(editor.IsVisible);
                Assert.True(home.IsVisible);
            }
        }
        finally { await editor.RequestCloseAsync(); home.Close(); main.Cleanup(); }
    }
}
