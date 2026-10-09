using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDraftDoesNotBlockSavedProjectAndNextDraft(bool corrupt)
    {
        await using var scope = new RecordingScope();
        string path;
        await using (var handle = await new JsonProjectStore().CreateAsync(scope.Configuration.OutputDirectory, "saved"))
        {
            path = Path.Combine(handle.ProjectDirectory, "project.opencam");
            await handle.SaveDraftAsync(new(Guid.NewGuid(), handle.Current.ProjectId, handle.Current.Revision, 1, "old draft", []));
            if (corrupt) await File.WriteAllTextAsync(Path.Combine(handle.ProjectDirectory, "project.edits.json"), "{broken");
            else await handle.SaveAsync(handle.Current with { Revision = handle.Current.Revision + 1 }, handle.Current.Revision);
        }
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.OpenAsync(path)).Success);
        Assert.Equal(ProjectMode.Ready, project.Mode);
        Assert.False(project.HasRecoverableDraft);
        Assert.NotNull(project.DraftError);
        Assert.Equal("saved", project.Current!.Name);
        Assert.True(File.Exists(Path.Combine(project.ProjectDirectory!, "project.edits.invalid.json")));
        Assert.True((await project.RenameProjectAsync("new draft", project.Current.Revision)).Success);
        await UntilAsync(() => File.Exists(Path.Combine(project.ProjectDirectory!, "project.edits.json")) && project.DraftError is null);
    }

    [Fact]
    public async Task FailedPolicyReplacementKeepsLastDurableDraftRecoverable()
    {
        await using var scope = new RecordingScope();
        string path;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub()))
        {
            await project.CreateAsync(scope.Configuration.OutputDirectory, "saved");
            path = Path.Combine(project.ProjectDirectory!, "project.opencam");
            await project.RenameProjectAsync("durable draft", project.Current!.Revision);
            await UntilAsync(() => File.Exists(Path.Combine(project.ProjectDirectory!, "project.edits.json")));
            var obstruction = Path.Combine(project.ProjectDirectory!, "project.edits.json.bak");
            Directory.CreateDirectory(obstruction);
            Assert.False((await project.SetAutoExportOnStopAsync(false, project.Current.Revision)).Success);
            Directory.Delete(obstruction);
        }
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await reopened.OpenAsync(path)).Success);
        Assert.True(reopened.HasRecoverableDraft);
        Assert.True((await reopened.ResolveDraftAsync(true, reopened.Current!.Revision)).Success);
        Assert.Equal("durable draft", reopened.Current!.Name);
        Assert.False(reopened.Current.AutoExportOnStop);
        Assert.True((await reopened.SaveAsync(reopened.Current.Revision)).Success);
        Assert.Null(reopened.Current.RecoveryDraftBaseRevision);
        Assert.True((await reopened.CloseAsync()).Success);
        Assert.True((await reopened.OpenAsync(path)).Success);
        Assert.False(reopened.HasRecoverableDraft);
    }

    [Fact]
    public async Task FailedNewDraftDoesNotBlockDiscardAfterDiskRecovers()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.CreateAsync(scope.Configuration.OutputDirectory, "saved");
        await project.RenameProjectAsync("saved edit", project.Current!.Revision);
        await UntilAsync(() => File.Exists(Path.Combine(project.ProjectDirectory!, "project.edits.json")));
        await project.SaveAsync(project.Current.Revision);
        var obstruction = Path.Combine(project.ProjectDirectory!, "project.edits.json.bak");
        Directory.CreateDirectory(obstruction);
        await project.RenameProjectAsync("discard me", project.Current.Revision);
        Assert.False((await project.SetAutoExportOnStopAsync(false, project.Current.Revision)).Success);
        Directory.Delete(obstruction);
        Assert.True((await project.DiscardEditsAsync(project.Current.Revision)).Success);
        Assert.Equal("saved edit", project.Current.Name);
        Assert.False(project.IsDirty);
        Assert.True((await project.CloseAsync()).Success);
    }
}
