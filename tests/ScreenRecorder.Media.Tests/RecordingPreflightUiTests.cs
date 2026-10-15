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
    [Fact]
    public void LateCheckingReplyCannotReenableStartAfterTrialHasBegun()
    {
        var id = Guid.NewGuid(); var server = Guid.NewGuid(); var vm = new RecordingPreflightViewModel(id);
        var checking = PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true };
        Assert.True(vm.Apply(new(server, checking, Sequence: 1)));
        vm.ScreenConfirmed = true; vm.SilenceAcknowledged = true;
        Assert.True(vm.CanStart);
        Assert.True(vm.Apply(new(server, checking with { Phase = PreflightPhase.TrialRecording, HasFrame = false }, Sequence: 3)));
        Assert.False(vm.Apply(new(server, checking, Sequence: 2)));
        Assert.False(vm.CanStart);
        Assert.Equal(PreflightPhase.TrialRecording, vm.Status.Phase);
    }
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ActualDialogRequiresConfirmationsAndJoinsCaptureBeforeClosing()
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
        Assert.False(start.IsEnabled);
        model.ScreenConfirmed = true; model.SilenceAcknowledged = true;
        Assert.True(start.IsEnabled);
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
        var vm = new RecordingPreflightViewModel(id) { ScreenConfirmed = true, SilenceAcknowledged = true };
        vm.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true, Microphone = new(state) }));
        Assert.False(vm.CanStart);
    }
    [Fact]
    public void SilenceNeedsExplicitAcknowledgementAndTrialCannotMasqueradeAsLiveCheck()
    {
        var id = Guid.NewGuid(); var server = Guid.NewGuid();
        var vm = new RecordingPreflightViewModel(id);
        var status = PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1, Phase = PreflightPhase.Checking,
            HasFrame = true, Microphone = new(PreflightSourceState.Silent), SystemAudio = new(PreflightSourceState.Live, .5, .8) };
        vm.Apply(new(server, status));
        Assert.False(vm.CanStart);
        vm.ScreenConfirmed = true;
        Assert.False(vm.CanStart);
        vm.SilenceAcknowledged = true;
        Assert.True(vm.CanStart);
        vm.Apply(new(server, status with { Phase = PreflightPhase.Reviewing, HasTrial = true }));
        Assert.False(vm.CanStart);
        Assert.False(vm.Apply(new(Guid.NewGuid(), status)));
        Assert.False(vm.Apply(new(server, status with { OperationId = Guid.NewGuid() })));
        Assert.False(vm.CanStart);
    }
    [Fact]
    public void AllDisabledInputsClearlyRequireSilentRecordingAcknowledgement()
    {
        var id = Guid.NewGuid(); var vm = new RecordingPreflightViewModel(id);
        vm.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with { OperationId = id, ConfigurationRevision = 1,
            Phase = PreflightPhase.Checking, HasFrame = true }));
        vm.ScreenConfirmed = true;
        Assert.False(vm.CanStart);
        vm.SilenceAcknowledged = true;
        Assert.True(vm.CanStart);
        vm.Busy = true;
        Assert.False(vm.CanStart);
    }
}
