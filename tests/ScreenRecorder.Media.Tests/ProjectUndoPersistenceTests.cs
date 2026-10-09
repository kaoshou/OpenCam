// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectUndo_SaveAndNewRecordingDoNotEraseRenameHistory_OriginalSourcesRemain()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var id = project.Current!.Clips.Single().Id;
        var original = project.Current.Clips.Single().Name;
        Assert.True((await project.RenameClipAsync(id, "Edited", project.Current.Revision)).Success);
        Assert.True((await project.SaveAsync(project.Current.Revision)).Success);
        Assert.True(project.CanUndo);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
        Assert.Equal(original, project.Current.Clips[0].Name);
        Assert.Equal(2, project.Current.Clips.Length);
        Assert.Equal(2, project.Current.Sources.Length);
        Assert.True((await project.RedoAsync(project.Current.Revision)).Success);
        Assert.Equal("Edited", project.Current.Clips[0].Name);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        var manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
        Assert.True((await project.CloseAsync()).Success);
        Assert.True((await project.OpenAsync(manifest)).Success);
        Assert.Equal("Edited", project.Current!.Clips[0].Name);
        Assert.False(project.CanUndo);
        Assert.False(project.CanRedo);
        Assert.Empty(scope.Remuxer.Inputs);
    }

    [Fact]
    public async Task ProjectUndo_FailedSaveKeepsDirtyEditsAndUndo_RetryPersists()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var backup = Path.Combine(project.ProjectDirectory!, "project.opencam.bak");
        File.Move(backup, backup + ".preserved");
        Directory.CreateDirectory(backup);
        var clip = project.Current!.Clips.Single();
        Assert.True((await project.RenameClipAsync(clip.Id, "Changed", project.Current.Revision)).Success);
        Assert.False((await project.SaveAsync(project.Current.Revision)).Success);
        Assert.True(project.IsDirty);
        Assert.True(project.CanUndo);
        Assert.Equal("Changed", project.Current.Clips.Single().Name);
        Directory.Delete(backup);
        Assert.True((await project.SaveAsync(project.Current.Revision)).Success);
        Assert.False(project.IsDirty);
        Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
        Assert.Equal(clip.Name, project.Current.Clips.Single().Name);
    }
}
