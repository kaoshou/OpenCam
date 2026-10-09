using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":\"broken\"}")]
    public async Task HomeCanOpenValidProjectAfterCorruptProjectReportsError(string malformed)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })),
            () => true, () => Task.CompletedTask);
        string validPath;
        await using (var valid = await new JsonProjectStore().CreateAsync(scope.Configuration.OutputDirectory, "Valid"))
            validPath = Path.Combine(valid.ProjectDirectory, "project.opencam");
        var bad = Path.Combine(scope.Configuration.OutputDirectory, "project.opencam");
        await File.WriteAllTextAsync(bad, malformed);
        try
        {
            await main.OpenRecordingProjectAsync(bad);
            Assert.False(main.CanOpenContentEditor);
            Assert.False(string.IsNullOrWhiteSpace(main.StatusMessage));
            await main.OpenRecordingProjectAsync(validPath);
            Assert.Equal("Valid", main.CurrentRecordingProjectName);
            Assert.True(main.CanOpenContentEditor);
        }
        finally { main.Cleanup(); }
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
        await vm.SaveAsync();
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
