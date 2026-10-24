// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LiveStickConfirmsOverlayBeforeCaptureAndPreservesSelectedAudio(bool ready)
    {
        using var overlay = new LiveOverlay { Ready = ready };
        await using var scope = new RecordingScope(cursor: overlay);
        scope.Configuration.CursorEffect = CursorEffectMode.PresentationStick;
        scope.Configuration.AudioSource = AudioSourceType.SystemAndMicrophone;
        scope.Configuration.EditablePointerEnabled = true; // stale preference
        scope.Configuration.PresentationPointer = new(TeachingPointerStyle.HighContrast, TeachingPointerSize.Large);
        var expectedStarts = 1;
        scope.Factory.BeforeStart = () => Assert.Equal(expectedStarts, overlay.PresentationStarts);
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "live pointer")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        var engine = Assert.Single(scope.Factory.Created);
        Assert.False(engine.InputEditablePointer);
        Assert.Equal(ready ? CursorEffectMode.PresentationStick : CursorEffectMode.Default, engine.InputCursorEffect);
        Assert.Equal(AudioSourceType.SystemAndMicrophone, engine.InputAudioSource);
        Assert.Equal(ready, scope.Recorder.GetTelemetry().IsCursorEffectHealthy);
        Assert.Equal(scope.Configuration.PresentationPointer, overlay.LastOptions);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.False(overlay.IsRunning);
        expectedStarts = 2;
        scope.Configuration.PresentationPointer = new(TeachingPointerStyle.RoundShort, TeachingPointerSize.Small);
        var resumed = await project.StartAsync(scope.Configuration, Guid.NewGuid());
        Assert.True(resumed.Success, resumed.ErrorCode);
        Assert.Equal(2, overlay.PresentationStarts);
        Assert.Equal(scope.Configuration.PresentationPointer, overlay.LastOptions);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.False(overlay.IsRunning);
        Assert.All(project.Current!.Sources, source => Assert.Null(source.Pointer));
        Assert.Empty(Directory.GetFiles(project.ProjectDirectory!, "*.pointer", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LostLiveOverlayDoesNotStopOrRestartTheRecording()
    {
        using var overlay = new LiveOverlay();
        await using var scope = new RecordingScope(cursor: overlay);
        scope.Configuration.CursorEffect = CursorEffectMode.PresentationStick;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        overlay.LoseOverlay();
        Assert.Equal(RecordingState.Recording, scope.Recorder.CurrentState);
        Assert.True(Assert.Single(scope.Factory.Created).IsRunning);
        Assert.False(scope.Recorder.GetTelemetry().IsCursorEffectHealthy);
        Assert.True((await scope.Recorder.StopRecordingAsync()).Success);
    }

    [Fact]
    public async Task FatalRuntimeEngineFailureStopsOptionalOverlay()
    {
        using var overlay = new LiveOverlay();
        await using var scope = new RecordingScope(cursor: overlay);
        scope.Configuration.CursorEffect = CursorEffectMode.PresentationStick;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.True(overlay.IsRunning);
        Assert.Single(scope.Factory.Created).FailWhileRecording();
        Assert.Equal(RecordingState.Failed, scope.Recorder.CurrentState);
        Assert.False(overlay.IsRunning);
        Assert.Single(scope.Factory.Created);
    }

    [Fact]
    public async Task AudioRecoveryCleanupFailureStopsOptionalOverlay()
    {
        using var overlay = new LiveOverlay();
        await using var scope = new RecordingScope(cursor: overlay);
        scope.Configuration.CursorEffect = CursorEffectMode.PresentationStick;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        scope.Factory.FailCleanup = true;
        Assert.Single(scope.Factory.Created).LoseAudio();
        await UntilAsync(() => scope.Store.LastSavedState == RecordingState.Failed);
        Assert.Equal(RecordingState.Failed, scope.Recorder.CurrentState);
        Assert.False(overlay.IsRunning);
        Assert.Single(scope.Factory.Created);
    }

    [Fact]
    public async Task FailedRecordingStartupStopsTheOptionalOverlay()
    {
        using var overlay = new LiveOverlay();
        await using var scope = new RecordingScope(cursor: overlay);
        scope.Configuration.CursorEffect = CursorEffectMode.PresentationStick;
        scope.Factory.BeforeStart = () => throw new IOException("Injected recording startup failure.");
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.False(overlay.IsRunning);
        Assert.True(overlay.StopCalls > 0);
    }

    private sealed class LiveOverlay : ICursorHighlightService, IPresentationPointerOverlay
    {
        public bool Ready = true;
        public int PresentationStarts, StopCalls;
        public TeachingPointerOptions? LastOptions;
        public bool IsRunning { get; private set; }
        public event EventHandler? OverlayFailed;
        public Task<bool> StartPresentationAsync(TeachingPointerOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentationStarts++;
            LastOptions = options;
            IsRunning = Ready;
            return Task.FromResult(Ready);
        }
        public void Start(CursorEffectMode mode) => IsRunning = mode is not (CursorEffectMode.Default or CursorEffectMode.Hidden);
        public void Stop() { StopCalls++; IsRunning = false; }
        public void Dispose() => Stop();
        public void LoseOverlay() { IsRunning = false; OverlayFailed?.Invoke(this, EventArgs.Empty); }
    }
}

public sealed class LivePointerWarningTests
{
    [Fact]
    public void MissingDecorationIsVisibleWithoutClaimingVideoOrAudioFailure()
    {
        const string current = "recording";
        var telemetry = new RecorderTelemetry { State = RecordingState.Recording, IsVideoCaptureHealthy = true,
            IsEncoderHealthy = true, IsCursorEffectHealthy = false };
        var status = ScreenRecorder.UI.ViewModels.MainViewModel.ResolveRecorderHealthStatus(telemetry, current);
        Assert.NotEqual(current, status);
    }
}
