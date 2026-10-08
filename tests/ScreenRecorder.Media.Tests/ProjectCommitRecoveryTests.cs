// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectCommitRecovery_PartialCommitFailure_CanRetryWithoutStaleRevisionOrDuplicates()
    {
        await using var scope = new RecordingScope();
        var probe = new ProjectProbeStub { FailAt = 2 };
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe);
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        // Simulate the existing audio-recovery pipeline creating an additional segment internally.
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        Assert.True((await scope.Recorder.ResumeRecordingAsync()).Success);
        Assert.False((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Single(project.Current!.Sources);
        var retry = await project.FinishAsync(Guid.NewGuid());
        Assert.True(retry.Success, retry.ErrorCode);
        Assert.Equal(2, project.Current.Sources.Length);
        Assert.Equal(2, project.Current.Clips.Length);
    }

    [Theory]
    [InlineData("before-journal")]
    [InlineData("after-journal")]
    [InlineData("after-manifest")]
    [InlineData("pending-session-save")]
    public async Task ProjectCommitRecovery_ReopenReconcilesExactlyOnce(string crashPoint)
    {
        await using var scope = new RecordingScope();
        var probe = new ProjectProbeStub();
        string manifest;
        ScreenRecorder.Core.Models.RecordingSession session;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            session = scope.Recorder.CurrentSession!;
            Assert.True((await scope.Recorder.StopRecordingAsync("simulated recorder exit")).Success);
            // No coordinator commit: the finalized session is the durable recovery evidence.
        }
        if (crashPoint == "pending-session-save")
        {
            session.SegmentFilePaths.Clear();
            await new ScreenRecorder.Infrastructure.Session.JsonRecordingSessionStore().SaveSessionAsync(session);
        }
        else if (crashPoint != "before-journal")
        {
            await using var handle = await new JsonProjectStore().OpenAsync(manifest);
            var path = Path.GetRelativePath(handle.ProjectDirectory, session.SegmentFilePaths.Single()).Replace('\\', '/');
            if (crashPoint == "after-manifest")
                await new ProjectSegmentCommitter(probe).CommitAsync(handle, session, Guid.NewGuid());
            var sourceId = handle.Current.Sources.FirstOrDefault()?.Id ?? Guid.NewGuid();
            await new ProjectWriteJournal().WriteIntentAsync(handle,
                new(Guid.NewGuid(), sourceId, session.SessionId, path, handle.Current.Revision));
        }
        for (var i = 0; i < 2; i++)
        {
            await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe);
            var open = await reopened.OpenAsync(manifest);
            Assert.True(open.Success, open.ErrorCode);
            Assert.Single(reopened.Current!.Sources);
            Assert.Single(reopened.Current.Clips);
            Assert.True((await reopened.CloseAsync()).Success);
        }
    }

    [Fact]
    public async Task ProjectCommitRecovery_MovedProjectRetainsPortableSessionMetadata()
    {
        await using var scope = new RecordingScope();
        var probe = new ProjectProbeStub();
        string original;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
            original = project.ProjectDirectory!;
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await scope.Recorder.StopRecordingAsync("simulated exit")).Success);
        }
        var moved = Path.Combine(scope.Configuration.OutputDirectory, "moved");
        Directory.Move(original, moved);
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe);
        var result = await reopened.OpenAsync(Path.Combine(moved, "project.opencam"));
        Assert.True(result.Success, result.ErrorCode);
        Assert.Single(reopened.Current!.Sources);
    }

    [Fact]
    public async Task ProjectCommitRecovery_MissingSourceCannotResume()
    {
        await using var scope = new RecordingScope();
        var probe = new ProjectProbeStub();
        string manifest;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
            File.Move(scope.Factory.Paths.Single(), scope.Factory.Paths.Single() + ".missing");
        }
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe);
        Assert.False((await reopened.OpenAsync(manifest)).Success);
        Assert.Equal(ProjectMode.Interrupted, reopened.Mode);
        Assert.False((await reopened.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Single(scope.Factory.Paths);
    }
}
