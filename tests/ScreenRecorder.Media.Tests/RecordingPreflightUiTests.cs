using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Preflight;
using Avalonia.Controls;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task PreflightLeaseIsReleasedWhenInitialMetadataSaveFailsBeforeEngineExists()
    {
        var lease = new ScreenRecorder.Recorder.Services.RecordingCaptureLease();
        await using var scope = new RecordingScope(captureLease: lease);
        scope.Store.OnSave = _ => Task.FromException(new IOException("injected initial metadata failure"));
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Empty(scope.Factory.Created);
        Assert.False(lease.IsOwned);
    }
    [Fact]
    public async Task PreflightNativeStopFailureIsNotHiddenBySuccessfulEncoderStop()
    {
        var source = new UnconfirmedInput();
        var engine = new EngineFactoryStub().Create(ScreenRecorder.Core.Enums.HardwareEncoderType.SoftwareCpu);
        var resources = new ScreenRecorder.Recorder.Services.PreflightCaptureResources(engine, [source]);
        await engine.StopRecordingAsync();
        await Assert.ThrowsAsync<IOException>(() => resources.StopSourcesAsync(default));
        source.Fail = false;
        await resources.DisposeAsync();
        Assert.True(source.StopCalls >= 2);
    }
    private sealed class UnconfirmedInput : ScreenRecorder.Core.Interfaces.ISystemAudioLoopbackCapture
    {
        public bool Fail = true; public int StopCalls;
        public bool IsSupported => true;
        public bool IsCapturing => Fail;
        public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
        public Task<ScreenRecorder.Core.Interfaces.SystemAudioCaptureInfo?> StartCaptureAsync(int monitorIndex, CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopCaptureAsync(CancellationToken ct = default)
        { StopCalls++; return Fail ? Task.FromException(new IOException("Native source stop unconfirmed")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // Existing home-flow tests use synthetic engines and explicit simulated user approval.
    private static ScreenRecorder.UI.ViewModels.MainViewModel CreatePreflightApprovedMain() => new(forScreenshot: true)
    { RequestRecordingPreflight = (configuration, _) => Task.FromResult<ScreenRecorder.Core.Models.RecordingConfiguration?>(configuration) };

    [Fact]
    public async Task CancellingPreflightCreatesNeitherProjectNorRecordingSession()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            new ScreenRecorder.Infrastructure.Projects.JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        var calls = 0;
        main.ConfigureRecordingContent(new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Canceled check");
        main.RequestRecordingPreflight = (_, _) => { calls++; return Task.FromResult<ScreenRecorder.Core.Models.RecordingConfiguration?>(null); };
        try
        {
            await main.StartRecordingAsync();
            Assert.Equal(1, calls);
            Assert.Null(coordinator.Current);
            Assert.Null(scope.Recorder.CurrentSession);
            Assert.Empty(scope.Factory.Paths);
        }
        finally { main.Cleanup(); }
    }
}

public class RecordingPreflightUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ScreenRecorder.Core.Enums.AudioSourceType.None, false, false)]
    [InlineData(ScreenRecorder.Core.Enums.AudioSourceType.SystemOnly, false, true)]
    [InlineData(ScreenRecorder.Core.Enums.AudioSourceType.MicrophoneOnly, true, false)]
    [InlineData(ScreenRecorder.Core.Enums.AudioSourceType.SystemAndMicrophone, true, true)]
    public async Task UnselectedAudioNeverDisplaysLiveLevels(ScreenRecorder.Core.Enums.AudioSourceType audio, bool mic, bool system)
    {
        var server = Guid.NewGuid(); long sequence = 0;
        var window = new RecordingPreflightWindow(new() { AudioSource = audio }, (command, request, ct) =>
        {
            if (command == "BeginPreflight") Assert.Equal(audio, request.Configuration!.AudioSource);
            // Even an inconsistent live report cannot override the user's selection.
            var status = PreflightStatus.Empty with { OperationId = request.OperationId, ConfigurationRevision = 1,
                Phase = command == "EndPreflight" ? PreflightPhase.Closed : PreflightPhase.Checking,
                Microphone = new(PreflightSourceState.Live, .7, .8), SystemAudio = new(PreflightSourceState.Live, .6, .8) };
            return Task.FromResult(new IpcResponse { Success = true, DataJson = System.Text.Json.JsonSerializer.Serialize(
                new PreflightReply(server, status, Sequence: ++sequence)) });
        }, (_, _) => throw new InvalidOperationException("No preview requested"));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (((RecordingPreflightViewModel)window.DataContext!).Status.Phase != PreflightPhase.Checking)
                await Task.Delay(10, deadline.Token);
            foreach (var (prefix, enabled) in new[] { ("Microphone", mic), ("System", system) })
            {
                Assert.Equal(enabled, window.FindControl<ProgressBar>(prefix + "Meter")!.IsVisible);
                Assert.Equal(enabled, window.FindControl<Button>(prefix == "Microphone" ? "DisableMicrophone" : "DisableSystem")!.IsVisible);
                if (!enabled)
                {
                    Assert.Equal(0, window.FindControl<ProgressBar>(prefix + "Meter")!.Value);
                    Assert.Equal(ScreenRecorder.UI.Localization.LanguageManager.Instance["PreflightAudioNotRecorded"],
                        window.FindControl<TextBlock>(prefix == "Microphone" ? "MicrophoneStatus" : "SystemStatus")!.Text);
                }
            }
        }
        finally { window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
    [Theory]
    [InlineData(PreflightPhase.Checking, PreflightSourceState.Waiting, "PreflightSourceWaiting")]
    [InlineData(PreflightPhase.TrialRecording, PreflightSourceState.Waiting, "PreflightAudioTrialWaiting")]
    [InlineData(PreflightPhase.TrialRecording, PreflightSourceState.Live, "PreflightSourceLive")]
    [InlineData(PreflightPhase.TrialRecording, PreflightSourceState.Silent, "PreflightSourceSilent")]
    [InlineData(PreflightPhase.Reviewing, PreflightSourceState.Waiting, "PreflightAudioReview")]
    [InlineData(PreflightPhase.TrialRecording, PreflightSourceState.Disabled, "PreflightSourceDisabled")]
    [InlineData(PreflightPhase.Checking, PreflightSourceState.Unavailable, "PreflightSourceUnavailable")]
    [InlineData(PreflightPhase.TrialRecording, PreflightSourceState.Unavailable, "PreflightSourceUnavailable")]
    public void AudioStatusDistinguishesInactiveMonitoringFromMissingSamples(PreflightPhase phase, PreflightSourceState state, string key)
    {
        Assert.Equal(key, RecordingPreflightViewModel.AudioStatusKey(phase, state));
    }
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw)]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs)]
    public async Task PreviewShowsLoadingTrialReadyAndFailureInsteadOfUnexplainedBlackArea(ScreenRecorder.Core.Localization.AppLanguage language)
    {
        var strings = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var previous = strings.CurrentLanguage; strings.CurrentLanguage = language;
        var server = Guid.NewGuid(); long sequence = 0;
        var status = PreflightStatus.Empty with { Phase = PreflightPhase.Preparing };
        var trialCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new RecordingPreflightWindow(new(), async (command, request, ct) =>
        {
            if (command == "RecordPreflightTrial")
            {
                await trialCommand.Task.WaitAsync(ct);
                status = status with { Phase = PreflightPhase.TrialRecording, HasFrame = false,
                    Microphone = new(PreflightSourceState.Waiting), SystemAudio = new(PreflightSourceState.Waiting) };
            }
            if (command == "EndPreflight") status = status with { Phase = PreflightPhase.Closed, HasFrame = false };
            var reply = new PreflightReply(server, status with { OperationId = request.OperationId, ConfigurationRevision = 1 },
                Sequence: Interlocked.Increment(ref sequence));
            return new IpcResponse { Success = true, DataJson = System.Text.Json.JsonSerializer.Serialize(reply) };
        }, async (request, ct) =>
        {
            frameRequested.TrySetResult(); await frameReady.Task.WaitAsync(ct);
            return new ProjectReply(true, null, ProjectSnapshot.Closed with { ServerInstanceId = server })
            { Frame = new(1, 0, request.OperationId, [255, 0, 0, 255]) { PixelWidth = 1, PixelHeight = 1 } };
        });
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(10, deadline.Token); }
        try
        {
            Assert.Equal(strings["PreflightPreviewLoading"], window.FindControl<TextBlock>("PreviewMessage")!.Text);
            window.Show(); window.UpdateLayout();
            Assert.Equal(620, window.Height);
            Assert.Equal(540, window.MinHeight);
            var placeholder = window.FindControl<Border>("PreviewPlaceholder")!;
            var message = window.FindControl<TextBlock>("PreviewMessage")!;
            var hint = window.FindControl<TextBlock>("PreviewHint")!;
            var activity = window.FindControl<ProgressBar>("PreviewActivity")!;
            var start = window.FindControl<Button>("StartButton")!;
            Assert.True(placeholder.IsVisible); Assert.True(activity.IsVisible);
            Assert.Equal(strings["PreflightPreviewLoading"], message.Text); Assert.False(start.IsEnabled);
            status = status with { Phase = PreflightPhase.Checking, HasFrame = true };
            await frameRequested.Task.WaitAsync(deadline.Token);
            Assert.True(placeholder.IsVisible); // A status reply alone is not a presented frame.
            frameReady.TrySetResult();
            await Until(() => !placeholder.IsVisible);
            Assert.NotNull(window.FindControl<Image>("Preview")!.Source);
            window.FindControl<Button>("TrialButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(placeholder.IsVisible); // Feedback appears before the trial command returns.
            Assert.Equal(strings["PreflightTrialRecording"], message.Text);
            Assert.Equal(strings["PreflightTrialPreviewHint"], hint.Text);
            trialCommand.TrySetResult();
            await Until(() => ((RecordingPreflightViewModel)window.DataContext!).Status.Phase == PreflightPhase.TrialRecording);
            Assert.False(start.IsEnabled);
            Assert.Equal(strings["PreflightAudioTrialWaiting"], window.FindControl<TextBlock>("MicrophoneStatus")!.Text);
            Assert.Equal(strings["PreflightAudioTrialWaiting"], window.FindControl<TextBlock>("SystemStatus")!.Text);
            Assert.True(window.FindControl<ProgressBar>("MicrophoneMeter")!.IsVisible);
            status = status with { Microphone = new(PreflightSourceState.Live, .65, .8), SystemAudio = new(PreflightSourceState.Silent, .01, .02) };
            await Until(() => window.FindControl<ProgressBar>("MicrophoneMeter")!.Value == .65);
            Assert.Equal(strings["PreflightSourceLive"], window.FindControl<TextBlock>("MicrophoneStatus")!.Text);
            Assert.Equal(strings["PreflightSourceSilent"], window.FindControl<TextBlock>("SystemStatus")!.Text);
            status = status with { Phase = PreflightPhase.Reviewing, HasTrial = true, HasFrame = false };
            await Until(() => message.Text == strings["PreflightTrialReady"]);
            Assert.True(placeholder.IsVisible); Assert.False(activity.IsVisible);
            Assert.Equal(strings["PreflightAudioReview"], window.FindControl<TextBlock>("MicrophoneStatus")!.Text);
            Assert.False(window.FindControl<ProgressBar>("MicrophoneMeter")!.IsVisible);
            Assert.True(window.FindControl<Button>("ReplayButton")!.IsEnabled);
            Assert.False(start.IsEnabled);
            status = status with { Phase = PreflightPhase.Failed, ErrorCode = "InjectedFailure" };
            await Until(() => message.Text == strings["PreflightPreviewFailed"]);
            Assert.True(placeholder.IsVisible); Assert.False(activity.IsVisible); Assert.False(start.IsEnabled);
        }
        finally
        {
            frameReady.TrySetResult(); trialCommand.TrySetResult(); window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); strings.CurrentLanguage = previous;
        }
    }

    [Fact]
    public void LateCheckingReplyCannotReenableStartAfterTrialHasBegun()
    {
        var id = Guid.NewGuid(); var server = Guid.NewGuid(); var vm = new RecordingPreflightViewModel(id);
        var checking = PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true };
        Assert.True(vm.Apply(new(server, checking, Sequence: 1)));
        Assert.True(vm.CanStart);
        Assert.True(vm.Apply(new(server, checking with { Phase = PreflightPhase.TrialRecording, HasFrame = false }, Sequence: 3)));
        Assert.False(vm.Apply(new(server, checking, Sequence: 2)));
        Assert.False(vm.CanStart);
        Assert.Equal(PreflightPhase.TrialRecording, vm.Status.Phase);
    }
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ActualDialogUsesSingleConfirmationAndJoinsCaptureBeforeClosing()
    {
        var capture = new ScreenRecorder.Recorder.Services.RecordingCaptureLease();
        var source = new WindowSession();
        await using var coordinator = new ScreenRecorder.Recorder.Services.PreflightCoordinator(capture, () => source);
        await using var dispatcher = new ScreenRecorder.Recorder.Services.PreflightIpcDispatcher(coordinator);
        var window = new RecordingPreflightWindow(new() { AudioSource = ScreenRecorder.Core.Enums.AudioSourceType.None },
            (command, request, ct) => dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) }),
            (request, ct) => dispatcher.DispatchMediaAsync(new() { MessageType = "GetPreflightFrame", PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) }));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var model = Assert.IsType<RecordingPreflightViewModel>(window.DataContext);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!model.Status.HasFrame || model.Status.Phase != PreflightPhase.Checking) await Task.Delay(10, deadline.Token);
        window.UpdateLayout();
        var start = window.FindControl<Avalonia.Controls.Button>("StartButton")!;
        Assert.True(start.IsEnabled);
        Assert.Null(window.FindControl<CheckBox>("ScreenAcknowledgement"));
        Assert.Null(window.FindControl<CheckBox>("SilentAcknowledgement"));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, start.HorizontalContentAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, start.VerticalContentAlignment);
        Assert.InRange(start.Bounds.Height, 25, 48);
        start.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await closed.Task.WaitAsync(deadline.Token);
        Assert.False(capture.IsOwned);
        Assert.True(source.Stopped);
    }
    private sealed class WindowSession : ScreenRecorder.Recorder.Services.IPreflightLiveSession
    {
        public bool Stopped;
        public Task StartAsync(ScreenRecorder.Core.Models.RecordingConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty with { Phase = PreflightPhase.Checking, HasFrame = true };
        public ProjectFrameReply? ReadFrame() => new(0, 0, Guid.Empty, [255, 0, 0, 255]) { PixelWidth = 1, PixelHeight = 1 };
        public Task StopAsync(CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Theory]
    [InlineData(PreflightSourceState.Waiting)]
    [InlineData(PreflightSourceState.Unavailable)]
    public void MissingOrUnreadableInputCannotBeAcknowledgedAsSilent(PreflightSourceState state)
    {
        var id = Guid.NewGuid();
        var vm = new RecordingPreflightViewModel(id);
        vm.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true, Microphone = new(state) }));
        Assert.False(vm.CanStart);
    }
    [Fact]
    public void SilenceShowsNoticeAndTrialCannotMasqueradeAsLiveCheck()
    {
        var id = Guid.NewGuid(); var server = Guid.NewGuid();
        var vm = new RecordingPreflightViewModel(id);
        var status = PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1, Phase = PreflightPhase.Checking,
            HasFrame = true, Microphone = new(PreflightSourceState.Silent), SystemAudio = new(PreflightSourceState.Live, .5, .8) };
        vm.Apply(new(server, status));
        Assert.True(vm.ShowSilenceNotice);
        Assert.True(vm.CanStart);
        vm.Apply(new(server, status with { Phase = PreflightPhase.Reviewing, HasTrial = true }));
        Assert.False(vm.CanStart);
        Assert.False(vm.Apply(new(Guid.NewGuid(), status)));
        Assert.False(vm.Apply(new(server, status with { OperationId = Guid.NewGuid() })));
        Assert.False(vm.CanStart);
    }
    [Fact]
    public void AllDisabledInputsShowNoticeButDoNotRequireExtraCheckbox()
    {
        var id = Guid.NewGuid(); var vm = new RecordingPreflightViewModel(id);
        vm.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true }));
        Assert.True(vm.ShowSilenceNotice);
        Assert.True(vm.CanStart);
        vm.Busy = true;
        Assert.False(vm.CanStart);
    }
}
