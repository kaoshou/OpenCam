// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectInsertion_IntentIsDurableBeforeCaptureAndEmptyOutputDoesNotSplit()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Empty insertion")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        var original = project.Current!;
        var operation = Guid.NewGuid();
        var witnessed = false;
        scope.Factory.BeforeStart = () =>
        {
            var json = File.ReadAllText(Path.Combine(project.ProjectDirectory!, "project.insertions.json"));
            Assert.Contains(operation.ToString(), json);
            Assert.Contains("insert_" + operation.ToString("N"), json);
            witnessed = true;
        };
        scope.Factory.EmptyOutput = true;
        await project.StartInsertionAsync(scope.Configuration, original.Revision, TimeSpan.TicksPerSecond / 2, operation);
        Assert.True(witnessed);
        await project.FinishAsync(Guid.NewGuid());
        Assert.Equal(original.Clips, project.Current!.Clips);
        Assert.Equal(original.Sources, project.Current.Sources);
    }

    [Fact]
    public async Task ProjectInsertion_IpcViewModelUsesConfirmedPositionAndRejectsChangedTimeline()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(project);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "UI insertion");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        var revision = vm.State.Revision;
        await vm.StartInsertionAsync(scope.Configuration, TimeSpan.TicksPerSecond / 2, revision - 1);
        Assert.Equal(ProjectMode.Paused, project.Mode);
        Assert.Single(scope.Factory.Paths);
        await vm.StartInsertionAsync(scope.Configuration, TimeSpan.TicksPerSecond / 2, revision);
        Assert.Null(vm.Error);
        Assert.Equal(ProjectMode.Recording, project.Mode);
        await vm.FinishAsync();
        Assert.Equal(3, vm.Clips.Count);
        Assert.Equal(500, vm.Clips[0].OutPts);
    }

    [Fact]
    public async Task ProjectInsertion_RecordsAtPlayheadWithOneUndoAndNormalResumeStillAppends()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Insertion")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var original = project.Current!.Clips;
        var op = Guid.NewGuid();
        var revision = project.Current.Revision;
        var result = await project.StartInsertionAsync(scope.Configuration, revision, TimeSpan.TicksPerSecond / 2, op);
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(original, project.Current.Clips);
        var count = scope.Factory.Paths.Count;
        Assert.True((await project.StartInsertionAsync(scope.Configuration, revision, TimeSpan.TicksPerSecond / 2, op)).Success);
        Assert.Equal(count, scope.Factory.Paths.Count);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Equal(3, project.Current.Clips.Length);
        Assert.Equal(500, project.Current.Clips[0].OutPts);
        Assert.Equal(500, project.Current.Clips[2].InPts);
        Assert.NotEqual(original[0].SourceId, project.Current.Clips[1].SourceId);
        Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
        Assert.Equal(original, project.Current.Clips);
        Assert.Equal(2, project.Current.Sources.Length);
        Assert.True((await project.RedoAsync(project.Current.Revision)).Success);
        var inserted = project.Current.Clips;
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.Equal(inserted, project.Current.Clips.Take(3));
        Assert.Equal(4, project.Current.Clips.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectInsertion_ReopensFinalizedCaptureExactlyOnceEvenAfterUndo(bool undoBeforeReopen)
    {
        await using var scope = new RecordingScope();
        string manifest;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub()))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Recovery")).Success);
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            Assert.True((await project.StartInsertionAsync(scope.Configuration, project.Current!.Revision,
                TimeSpan.TicksPerSecond / 2, Guid.NewGuid())).Success);
            if (undoBeforeReopen)
            {
                Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
                Assert.True((await project.UndoAsync(project.Current.Revision)).Success);
                Assert.True((await project.SaveAsync(project.Current.Revision)).Success);
            }
            else Assert.True((await scope.Recorder.StopRecordingAsync("simulated process exit")).Success);
        }
        for (var i = 0; i < 2; i++)
        {
            await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
            var open = await reopened.OpenAsync(manifest);
            Assert.True(open.Success, open.ErrorCode);
            Assert.Equal(2, reopened.Current!.Sources.Length);
            Assert.Equal(undoBeforeReopen ? 1 : 3, reopened.Current.Clips.Length);
            if (!undoBeforeReopen)
            {
                Assert.Equal(500, reopened.Current.Clips[0].OutPts);
                Assert.Equal(500, reopened.Current.Clips[2].InPts);
            }
            Assert.True((await reopened.CloseAsync()).Success);
        }
    }

    [Fact]
    public async Task ProjectInsertion_RecoveryConflictDoesNotGuessOrDeleteNewSource()
    {
        await using var scope = new RecordingScope();
        string manifest;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub()))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Conflict")).Success);
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            Assert.True((await project.StartInsertionAsync(scope.Configuration, project.Current!.Revision,
                TimeSpan.TicksPerSecond / 2, Guid.NewGuid())).Success);
            Assert.True((await scope.Recorder.StopRecordingAsync("simulated exit")).Success);
        }
        await using (var handle = await new JsonProjectStore().OpenAsync(manifest))
        {
            var current = handle.Current;
            await handle.SaveAsync(current with { Revision = current.Revision + 1, Name = "Changed" }, current.Revision);
        }
        var bytes = await File.ReadAllBytesAsync(scope.Factory.Paths.Last());
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.False((await reopened.OpenAsync(manifest)).Success);
        Assert.Equal(ProjectMode.Interrupted, reopened.Mode);
        Assert.Single(reopened.Current!.Clips);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scope.Factory.Paths.Last()));
    }
}
