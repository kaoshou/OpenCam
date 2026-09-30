// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task LostCaptureSafelyStopsAndPreservesRecordedData()
    {
        await using var scope = new RecordingScope();
        scope.Factory.UseModern = true;
        scope.Health.Healthy = false;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        await UntilAsync(() => scope.Recorder.CurrentState == RecordingState.Completed);
        Assert.Single(scope.Remuxer.Inputs);
        Assert.True(File.Exists(scope.Remuxer.Inputs[0]));
        Assert.Single(scope.Factory.Created);
    }
    [Fact]
    public async Task CaptureFailureFallsBackOnceWithoutChangingEncoderOrPreference()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.WindowsCaptureMode = WindowsCaptureMode.ModernExperimental;
        scope.Factory.UseModern = scope.Factory.FailCapture = true;
        var start = await scope.Recorder.StartRecordingAsync(scope.Configuration);
        Assert.True(start.Success, start.ErrorMessage);
        Assert.Equal(2, scope.Factory.Created.Count);
        Assert.All(scope.Factory.Requested, e => Assert.Equal(HardwareEncoderType.IntelQsv, e));
        Assert.Equal(CaptureBackend.Gdi, scope.Recorder.CurrentSession!.CaptureSelection!.Backend);
        Assert.Equal(CaptureFallbackReason.StartupFailed, scope.Recorder.GetTelemetry().CaptureSelection!.FallbackReason);
        Assert.Equal(WindowsCaptureMode.ModernExperimental, scope.Configuration.WindowsCaptureMode);
        Assert.Empty(scope.Selection.Failed);
        Assert.Single(scope.Recorder.CurrentSession.SegmentFilePaths);
        Assert.Single(Directory.GetFiles(scope.Recorder.CurrentSession.WorkingDirectory, "failed_attempt_*.mkv"));
        Assert.NotEqual(scope.Factory.Paths[0], scope.Factory.Paths[1]);
    }

    [Fact]
    public async Task ResumePinsCaptureAndDoesNotFallbackAfterValidData()
    {
        await using var scope = new RecordingScope();
        scope.Factory.UseModern = true;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        var pinned = scope.Recorder.CurrentSession!.CaptureSelection;
        Assert.NotNull(pinned);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        scope.Factory.FailCapture = true;
        Assert.False((await scope.Recorder.ResumeRecordingAsync()).Success);
        Assert.Equal(RecordingState.Paused, scope.Recorder.CurrentState);
        Assert.Equal(2, scope.Factory.Created.Count);
        Assert.Equal(pinned, scope.Factory.Captures[1]);
        Assert.Single(scope.Recorder.CurrentSession.SegmentFilePaths);
        Assert.True(File.Exists(scope.Recorder.CurrentSession.SegmentFilePaths[0]));
    }

    [Fact]
    public async Task CaptureCleanupFailurePreventsFallback()
    {
        await using var scope = new RecordingScope();
        scope.Factory.UseModern = scope.Factory.FailCapture = scope.Factory.FailCleanup = true;
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Created);
    }

    [Fact]
    public async Task CaptureMetadataFailureAfterHandshakeKeepsDataAndDoesNotRetry()
    {
        await using var scope = new RecordingScope();
        scope.Factory.UseModern = true;
        scope.Store.FailNextCommittedSave = true;
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Created);
        Assert.Single(scope.Recorder.CurrentSession!.SegmentFilePaths);
        Assert.Equal(CaptureBackend.DesktopDuplication, scope.Recorder.CurrentSession.CaptureSelection!.Backend);
    }
}
