// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task RecordingContentCanvas_FirstVerifiedSourceSetsDimensionsAndCaptureRateOnlyOnce()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.Fps = 60;
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Equal(new ProjectCanvas(320, 240, new(60, 1)), project.Current!.Canvas);
        // Deleting all retained clips does not erase the established output format.
        Assert.True((await project.ApplyEditAsync(new ProjectClipEdit.Remove(project.Current.Clips[0].Id),
            project.Current.Revision, Guid.NewGuid())).Success);
        scope.Configuration.Fps = 30;
        Assert.True((await project.SaveAsync(project.Current.Revision)).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.Equal(new ProjectCanvas(320, 240, new(60, 1)), project.Current.Canvas);
        Assert.Equal(2, project.Current.Sources.Length);
    }

    [Fact]
    public async Task RecordingContentCanvas_DurableIntentRetainsRateAcrossRecovery()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.Fps = 60;
        string manifest;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub { Fail = true }))
        {
            Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            Assert.False((await project.FinishAsync(Guid.NewGuid())).Success);
            Assert.Empty(project.Current!.Sources);
        }
        await using var reopened = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var result = await reopened.OpenAsync(manifest);
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(new ProjectCanvas(320, 240, new(60, 1)), reopened.Current!.Canvas);
    }
}
