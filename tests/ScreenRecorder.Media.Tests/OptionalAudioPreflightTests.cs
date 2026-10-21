using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Preflight;

namespace ScreenRecorder.Media.Tests;

public class OptionalAudioPreflightTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task RecheckFailureDoesNotLeaveDialogPermanentlyBusy()
    {
        var server = Guid.NewGuid(); long sequence = 0;
        var rejectStop = true;
        var window = new RecordingPreflightWindow(new(), (command, request, ct) =>
        {
            if (command == "EndPreflight" && rejectStop)
                return Task.FromResult(new IpcResponse { Success = false, ErrorMessage = "injected stop rejection" });
            return Task.FromResult(new IpcResponse { Success = true, DataJson = JsonSerializer.Serialize(new PreflightReply(server,
                PreflightStatus.Empty with { OperationId = request.OperationId, ConfigurationRevision = 1,
                    Phase = command == "EndPreflight" ? PreflightPhase.Closed : PreflightPhase.Checking }, Sequence: ++sequence)) });
        });
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            window.Show();
            var model = (RecordingPreflightViewModel)window.DataContext!;
            while (model.Status.Phase != PreflightPhase.Checking) await Task.Delay(10, deadline.Token);
            window.FindControl<Button>("RecheckButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!model.ConnectionFailed) await Task.Delay(10, deadline.Token);
            Assert.False(model.Busy);
            Assert.False(model.CanStart); // Capture shutdown was not acknowledged.
            rejectStop = false;
            var original = window.Request.OperationId;
            window.FindControl<Button>("RecheckButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (window.Request.OperationId == original ||
                ((RecordingPreflightViewModel)window.DataContext!).Status.Phase != PreflightPhase.Checking)
                await Task.Delay(10, deadline.Token);
            Assert.False(((RecordingPreflightViewModel)window.DataContext!).Busy);
            Assert.False(((RecordingPreflightViewModel)window.DataContext!).ConnectionFailed);
        }
        finally { rejectStop = false; window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw, "系統音訊", "麥克風")]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs, "System audio", "Microphone")]
    public async Task SilenceNoticeIdentifiesOnlyTheSilentSource(ScreenRecorder.Core.Localization.AppLanguage language, string silent, string live)
    {
        var strings = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var previous = strings.CurrentLanguage; strings.CurrentLanguage = language;
        var server = Guid.NewGuid(); long sequence = 0;
        var window = new RecordingPreflightWindow(new() { AudioSource = AudioSourceType.SystemAndMicrophone }, (command, request, ct) =>
            Task.FromResult(new IpcResponse { Success = true, DataJson = JsonSerializer.Serialize(new PreflightReply(server,
                PreflightStatus.Empty with { OperationId = request.OperationId, ConfigurationRevision = 1, AudioReady = true,
                    Phase = command == "EndPreflight" ? PreflightPhase.Closed : PreflightPhase.Checking,
                    Microphone = new(PreflightSourceState.Live, .5, .6), SystemAudio = new(PreflightSourceState.Silent) }, Sequence: ++sequence)) }));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            window.Show();
            while (((RecordingPreflightViewModel)window.DataContext!).Status.Phase != PreflightPhase.Checking) await Task.Delay(10, deadline.Token);
            var text = window.FindControl<TextBlock>("SilenceNotice")!.Text!;
            Assert.Contains(silent, text);
            Assert.DoesNotContain(live, text);
            Assert.True(window.FindControl<Button>("StartButton")!.IsEnabled);
        }
        finally
        {
            try { window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { strings.CurrentLanguage = previous; }
        }
    }

    [Theory]
    [InlineData(PreflightPhase.Checking)]
    [InlineData(PreflightPhase.Reviewing)]
    [InlineData(PreflightPhase.Failed)]
    public void SoundCheckIsOptionalWithoutInventingAudioReadiness(PreflightPhase phase)
    {
        var id = Guid.NewGuid();
        var model = new RecordingPreflightViewModel(id);
        model.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with { OperationId = id,
            Phase = phase, Microphone = new(PreflightSourceState.Unavailable),
            ErrorCode = phase == PreflightPhase.Failed ? "PreflightAudioUnavailable" : null }));
        Assert.True(model.CanStart);
        Assert.False(model.Status.AudioReady);
        Assert.False(model.CanTrial);
        model.ConnectionFailed = true;
        Assert.False(model.CanStart); // Unknown ownership is not an optional audio test.
    }

    [Fact]
    public async Task ConfirmWithoutAudioSamplesStillWaitsForCaptureShutdown()
    {
        var source = new WaitingSession();
        var lease = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(lease, () => source);
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var id = Guid.NewGuid();
        var begun = await Send(dispatcher, "BeginPreflight", new(id, Configuration: new()));
        var request = new PreflightRequest(id, begun.Status.ConfigurationRevision, begun.ServerInstanceId);
        using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while ((await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Checking)
            await Task.Delay(10, ready.Token);
        var response = await dispatcher.DispatchAsync(new() { MessageType = "EndPreflight",
            PayloadJson = JsonSerializer.Serialize(request with { Confirm = true }) });
        try
        {
            Assert.True(response.Success, response.ErrorMessage);
            Assert.True(lease.IsOwned);
            var pending = await Send(dispatcher, "GetPreflightStatus", request);
            Assert.False(pending.ConfigurationConfirmed);
            source.Release.TrySetResult();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while ((await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Closed)
                await Task.Delay(10, deadline.Token);
            Assert.False(lease.IsOwned);
            Assert.True((await Send(dispatcher, "CheckPreflightConfirmation", request with { Configuration = new() })).ConfigurationConfirmed);
        }
        finally { source.Release.TrySetResult(); }
    }

    [Fact]
    public async Task UnavailableAudioIdentityCannotBlockExplicitStartOrCacheAutomaticResumeApproval()
    {
        var source = new WaitingSession(); source.Release.TrySetResult();
        var lease = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(lease, () => source);
        await using var dispatcher = new PreflightIpcDispatcher(coordinator, _ => throw new IOException("audio endpoint unavailable"));
        var id = Guid.NewGuid();
        var begun = await Send(dispatcher, "BeginPreflight", new(id, Configuration: new()));
        var request = new PreflightRequest(id, begun.Status.ConfigurationRevision, begun.ServerInstanceId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while ((await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Checking)
            await Task.Delay(10, deadline.Token);
        await Send(dispatcher, "EndPreflight", request with { Confirm = true });
        while ((await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Closed)
            await Task.Delay(10, deadline.Token);
        Assert.False(lease.IsOwned);
        Assert.False((await Send(dispatcher, "CheckPreflightConfirmation", request with { Configuration = new() })).ConfigurationConfirmed);
    }

    [Fact]
    public async Task FailedAudioInitializationCanBeClosedForExplicitRecording()
    {
        var lease = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(lease, () => new PreflightAudioSession(
            () => throw new IOException("audio device unavailable")));
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var id = Guid.NewGuid();
        var begun = await Send(dispatcher, "BeginPreflight", new(id, Configuration: new() { AudioSource = AudioSourceType.SystemOnly }));
        var request = new PreflightRequest(id, begun.Status.ConfigurationRevision, begun.ServerInstanceId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        PreflightReply reply;
        while ((reply = await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Failed)
            await Task.Delay(10, deadline.Token);
        Assert.False(lease.IsOwned);
        request = request with { ConfigurationRevision = reply.Status.ConfigurationRevision };
        await Send(dispatcher, "EndPreflight", request with { Confirm = true });
        while ((await Send(dispatcher, "GetPreflightStatus", request)).Status.Phase != PreflightPhase.Closed)
            await Task.Delay(10, deadline.Token);
        Assert.False(lease.IsOwned);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task TrialPlaybackRecheckAndStartUseFreshAudioSession()
    {
        var lease = new RecordingCaptureLease();
        var directories = new List<string>();
        var starts = 0;
        var output = new Output();
        await using var coordinator = new PreflightCoordinator(lease, () => new PreflightAudioSession(
            () => new Capture(), storage: () => { var s = new PreflightTrialStorage(); directories.Add(s.DirectoryPath); return s; },
            validate: (_, _) => Task.FromResult(TimeSpan.FromSeconds(5)),
            playback: (s, duration) => new(s.OpenRead, () => output, () => new Decode(), duration)));
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var window = new RecordingPreflightWindow(new() { AudioSource = AudioSourceType.MicrophoneOnly },
            (command, request, ct) => { if (command == "BeginPreflight") starts++;
                return dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }); });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(10, deadline.Token); }
        void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show();
            await Until(() => window.FindControl<Button>("TrialButton")!.IsEnabled);
            Click("TrialButton");
            await Until(() => window.FindControl<Button>("ReplayButton")!.IsEnabled);
            var original = window.Request.OperationId;
            Click("ReplayButton");
            await output.Completed.Task.WaitAsync(deadline.Token);
            await Until(() => !((RecordingPreflightViewModel)window.DataContext!).Busy &&
                ((RecordingPreflightViewModel)window.DataContext!).Status.Phase == PreflightPhase.Reviewing);
            Click("RecheckButton");
            await Until(() => window.Request.OperationId != original &&
                ((RecordingPreflightViewModel)window.DataContext!).Status.Phase == PreflightPhase.Checking);
            var model = (RecordingPreflightViewModel)window.DataContext!;
            Assert.Null(model.Status.ErrorCode);
            Assert.True(model.Status.AudioReady);
            Assert.False(model.Status.HasTrial);
            Assert.All(directories, path => Assert.False(Directory.Exists(path)));
            Assert.Equal(2, starts);
            Assert.True(window.FindControl<Button>("StartButton")!.IsEnabled);
            Click("StartButton");
            Assert.NotNull(await window.Confirmation.WaitAsync(deadline.Token));
            Assert.False(lease.IsOwned);
        }
        finally { window.SetPreparationState(RecordingPreparationState.Started); }
    }

    private static async Task<PreflightReply> Send(PreflightIpcDispatcher dispatcher, string command, PreflightRequest request)
    {
        var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
        Assert.True(response.Success, response.ErrorMessage);
        return JsonSerializer.Deserialize<PreflightReply>(response.DataJson!)!;
    }
    private sealed class WaitingSession : IPreflightLiveSession
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(RecordingConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty with { Phase = PreflightPhase.Checking,
            Microphone = new(PreflightSourceState.Waiting) };
        public ProjectFrameReply? ReadFrame() => null;
        public Task StopAsync(CancellationToken ct) => Release.Task.WaitAsync(ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Capture : IPreflightAudioCapture
    {
        public bool IsRunning { get; private set; }
        public string? ErrorCode => null;
        public async Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)
        { if (trialOutput is not null) await trialOutput.WriteAsync(new byte[32], ct); IsRunning = true; }
        public Task StopAsync(CancellationToken ct) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public PreflightSourceResult ReadMicrophone(DateTimeOffset now) => new(PreflightSourceState.Live, .5, .6);
        public PreflightSourceResult ReadSystemAudio(DateTimeOffset now) => new(PreflightSourceState.Disabled);
    }
    private sealed class Decode : IProjectStreamingMediaProcess
    {
        public async Task RunStreamingAsync(ProjectMediaJob job, FileStream input, Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
        { using var pcm = new MemoryStream(new byte[8192]); await consume(pcm, ct); }
    }
    private sealed class Output : IProjectAudioOutput
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long PositionSamples => 0;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken ct) { Completed.TrySetResult(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
