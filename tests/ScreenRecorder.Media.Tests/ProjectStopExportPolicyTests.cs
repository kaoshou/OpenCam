using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ChangingExportPolicyPreservesPendingDraftImmediately()
    {
        await using var scope = new RecordingScope();
        string path;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub()))
        {
            await project.CreateAsync(scope.Configuration.OutputDirectory, "saved");
            path = Path.Combine(project.ProjectDirectory!, "project.opencam");
            await project.RenameProjectAsync("recover pending edit", project.Current!.Revision);
            Assert.True((await project.SetAutoExportOnStopAsync(false, project.Current.Revision)).Success);
        }
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await reopened.OpenAsync(path)).Success);
        Assert.Equal("saved", reopened.Current!.Name);
        Assert.False(reopened.Current.AutoExportOnStop);
        Assert.True(reopened.HasRecoverableDraft);
        Assert.True((await reopened.ResolveDraftAsync(true, reopened.Current.Revision)).Success);
        Assert.Equal("recover pending edit", reopened.Current.Name);
    }

    [Fact]
    public async Task StopWithoutExportDurablySavesRecording()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        await project.CreateAsync(scope.Configuration.OutputDirectory, "project");
        await project.SetAutoExportOnStopAsync(false, project.Current!.Revision);
        await project.StartAsync(scope.Configuration, Guid.NewGuid());
        Assert.True((await project.FinishAndExportAsync(Guid.NewGuid())).Success);
        Assert.Null(project.ExportStatus);
        Assert.Single(project.Current.Sources);
        Assert.Equal(0, export.Calls);
        var path = Path.Combine(project.ProjectDirectory!, "project.opencam");
        await project.CloseAsync();
        await project.OpenAsync(path);
        Assert.False(project.Current!.AutoExportOnStop);
        Assert.Single(project.Current.Clips);
    }

    [Fact]
    public async Task PolicyPersistsWithoutSavingEdits()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.CreateAsync(scope.Configuration.OutputDirectory, "saved");
        await project.RenameProjectAsync("unsaved", project.Current!.Revision);
        Assert.True((await project.SetAutoExportOnStopAsync(false, project.Current.Revision)).Success);
        Assert.True(project.IsDirty);
        var summary = await JsonProjectStore.ReadSummaryAsync(Path.Combine(project.ProjectDirectory!, "project.opencam"));
        Assert.Equal("saved", summary.Name);
    }
}
