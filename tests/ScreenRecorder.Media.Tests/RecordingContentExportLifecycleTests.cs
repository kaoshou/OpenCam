// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task RecordingContentExportLifecycleTests_EditedPauseUsesSavedSnapshotAndLostFinishDoesNotDuplicate()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        }
        var clips = project.Current!.Clips;
        Assert.True((await project.ApplyEditAsync(new ProjectClipEdit.Remove(clips[1].Id), project.Current.Revision, Guid.NewGuid())).Success);
        Assert.True((await project.ApplyEditAsync(new ProjectClipEdit.Move(clips[2].Id, clips[0].Id), project.Current.Revision, Guid.NewGuid())).Success);
        var operation = Guid.NewGuid();
        var finished = await project.FinishAndExportAsync(operation);
        Assert.True(finished.Success, finished.ErrorCode);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { clips[2].Id, clips[0].Id }, export.Snapshot!.Clips.Select(c => c.Id));
        Assert.Equal(project.SavedRevision, export.Snapshot.Revision);
        Assert.Equal(scope.Configuration.OutputDirectory, export.Destination);
        Assert.Equal(finished, await project.FinishAndExportAsync(operation));
        Assert.Equal(1, export.Calls);
        export.Complete.TrySetResult(new(false, null, "injected export failure"));
        await UntilAsync(() => project.ExportStatus?.State == RecordingExportState.Failed);
        Assert.Equal(ProjectMode.Ready, project.Mode);
        Assert.Equal(3, project.Current.Sources.Length);
        Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
        Assert.True((await project.RetryExportAsync(Guid.NewGuid())).Success);
        await UntilAsync(() => export.Calls == 2 && project.ExportStatus?.State == RecordingExportState.Failed);
        Assert.Equal(3, project.Current.Sources.Length);
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Null(project.ExportStatus);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task RecordingContentExportLifecycleTests_LongExportAllowsStatusButBlocksSourceOwnerChanges()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAndExportAsync(Guid.NewGuid())).Success);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await project.ReconcileRecorderStatusAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(RecordingExportState.Running, project.ExportStatus!.State);
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.False((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.False((await project.CloseAsync()).Success);
        Assert.False((await project.ApplyEditAsync(new ProjectClipEdit.Remove(project.Current!.Clips[0].Id),
            project.Current.Revision, Guid.NewGuid())).Success);
        Assert.False((await project.CancelExportAsync(Guid.NewGuid())).Success);
        Assert.True((await project.CancelExportAsync(project.ExportStatus.ExportId).WaitAsync(TimeSpan.FromSeconds(5))).Success);
        Assert.Equal(RecordingExportState.Canceled, project.ExportStatus.State);
        Assert.True((await project.CloseAsync()).Success);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
    }

    private sealed class ControlledContentExporter : IRecordingContentExporter
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DeferCancellation;
        public readonly TaskCompletionSource<RecordingExportResult> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RecordingProject? Snapshot;
        public string? Destination;
        public int Calls;
        public async Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
            string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct)
        {
            Assert.Equal(owner.Current.Revision, snapshot.Revision);
            Snapshot = snapshot;
            Destination = outputDirectory;
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            try { return await Complete.Task.WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (DeferCancellation) await ReleaseCancellation.Task;
                throw;
            }
        }
    }
}
