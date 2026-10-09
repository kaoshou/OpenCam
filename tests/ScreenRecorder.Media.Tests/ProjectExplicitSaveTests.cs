using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectExplicitSave_SaveAcknowledgementControlsDirtyState()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var controller = new ScreenRecorder.UI.Projects.RecordingContentController(new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) })), _ => Task.CompletedTask);
        await controller.ReplaceProjectAsync(scope.Configuration.OutputDirectory, "saved");
        await controller.Workspace.RenameProjectAsync("changed");
        Assert.True(controller.Workspace.State.IsDirty);
        Assert.True(controller.CanOpenEditor);
        controller.Workspace.RequestUnsavedDecision = () => Task.FromResult(ScreenRecorder.UI.Projects.UnsavedDecision.Cancel);
        await controller.ResumeAsync(scope.Configuration);
        Assert.Empty(scope.Factory.Paths);
        Assert.True(controller.Workspace.State.IsDirty);
        controller.Workspace.RequestUnsavedDecision = () => Task.FromResult(ScreenRecorder.UI.Projects.UnsavedDecision.Save);
        await controller.ResumeAsync(scope.Configuration);
        Assert.Single(scope.Factory.Paths);
        Assert.False(controller.Workspace.State.IsDirty);
        await controller.Workspace.FinishAsync();
    }

    [Fact]
    public async Task ProjectExplicitSave_EditUndoRedoDoNotSaveManifest()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "saved")).Success);
        var file = Path.Combine(project.ProjectDirectory!, "project.opencam");
        var original = await File.ReadAllBytesAsync(file);
        Assert.True((await project.RenameProjectAsync("unsaved", project.Current!.Revision)).Success);
        Assert.True(project.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(file));
        Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
        Assert.False(project.IsDirty);
        Assert.True((await project.RedoAsync(project.Current.Revision)).Success);
        Assert.True(project.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(file));
        Assert.True((await project.SaveAsync(project.Current.Revision)).Success);
        Assert.False(project.IsDirty);
        Assert.Contains("unsaved", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ProjectExplicitSave_CancelBlocksCloseSwitchRecordExport()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.CreateAsync(scope.Configuration.OutputDirectory, "saved");
        await project.RenameProjectAsync("unsaved", project.Current!.Revision);
        Assert.False((await project.CloseAsync()).Success);
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.False((await project.ExportAsync(scope.Configuration.OutputDirectory, project.Current!.Revision, Guid.NewGuid())).Success);
        Assert.Empty(scope.Factory.Paths);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public async Task ProjectExplicitSave_ResumeThenDiscardKeepsNewRecording()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid());
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        await project.RenameProjectAsync("saved rename", project.Current!.Revision);
        await project.SaveAsync(project.Current.Revision);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        var sources = project.Current.Sources;
        await project.RenameProjectAsync("discard this", project.Current.Revision);
        Assert.True((await project.DiscardEditsAsync(project.Current.Revision)).Success);
        Assert.Equal("saved rename", project.Current.Name);
        Assert.Equal(sources, project.Current.Sources);
        Assert.Equal(2, project.Current.Clips.Length);
    }

    [Fact]
    public async Task ProjectExplicitSave_DraftRecoveryRequiresChoice()
    {
        await using var scope = new RecordingScope();
        string path;
        await using (var first = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub()))
        {
            await first.CreateAsync(scope.Configuration.OutputDirectory, "saved");
            path = Path.Combine(first.ProjectDirectory!, "project.opencam");
            await first.RenameProjectAsync("recover me", first.Current!.Revision);
            await Task.Delay(750);
        }
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await reopened.OpenAsync(path)).Success);
        Assert.True(reopened.HasRecoverableDraft);
        Assert.Equal("saved", reopened.Current!.Name);
        Assert.True((await reopened.ResolveDraftAsync(true, reopened.Current.Revision)).Success);
        Assert.Equal("recover me", reopened.Current.Name);
        Assert.True(reopened.IsDirty);
    }
}
