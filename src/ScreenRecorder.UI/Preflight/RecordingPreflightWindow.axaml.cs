// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Preflight;

public partial class RecordingPreflightWindow : Window
{
    private readonly Func<string, PreflightRequest, CancellationToken, Task<IpcResponse>> _send;
    private readonly RecordingConfiguration _configuration;
    private RecordingPreflightViewModel _model = null!;
    private CancellationTokenSource _pollCancellation = new();
    private Task _poll = Task.CompletedTask;
    private bool _closing, _authorized, _cancelHandoff;
    private readonly TaskCompletionSource<RecordingConfiguration?> _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<RecordingConfiguration?> Confirmation => _confirmation.Task;
    public RecordingPreparationState? PreparationState { get; private set; }
    private string? _pendingAction;
    private readonly SemaphoreSlim _action = new(1, 1);
    private CancellationTokenSource _volumeDebounce = new();
    private Task _volumeUpdate = Task.CompletedTask;
    private bool _volumePending, _volumeChanged, _syncingVolumes;
    private int _appliedSystemVolume = 100, _appliedMicrophoneVolume = 100;
    public PreflightRequest Request { get; private set; } = new(Guid.NewGuid());
    private static LanguageManager Strings => LanguageManager.Instance;
    public RecordingPreflightWindow(RecordingConfiguration configuration,
        Func<string, PreflightRequest, CancellationToken, Task<IpcResponse>> send,
        Func<PreflightRequest, CancellationToken, Task<ProjectReply>>? frame = null, bool preparationOnly = false)
    {
        _configuration = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(configuration))!;
        _send = send;
        InitializeComponent();
        Title = Heading.Text = Strings["PreflightTitle"];
        Privacy.Text = Strings["PreflightPrivacy"];
        MicrophoneLabel.Text = Strings["PreflightMicrophone"]; SystemLabel.Text = Strings["PreflightSystem"];
        TrialButton.Content = Strings["PreflightTrial"]; ReplayButton.Content = Strings["PreflightMixReplay"];
        MixLabel.Text = Strings["PreflightMixLabel"];
        VolumeHint.Text = Strings["PreflightVolumeHint"];
        ResetVolumesButton.Content = Strings["PreflightVolumeReset"];
        SystemVolume.Value = _configuration.SystemAudioVolumePercent;
        MicrophoneVolume.Value = _configuration.MicrophoneVolumePercent;
        AutomationProperties.SetName(SystemVolume, Strings["PreflightSystemVolume"]);
        AutomationProperties.SetName(MicrophoneVolume, Strings["PreflightMicrophoneVolume"]);
        StopReplayButton.Content = Strings["PreflightStopReplay"]; RecheckButton.Content = Strings["PreflightRecheck"];
        DisableMicrophone.Content = Strings["PreflightDisableMic"]; DisableSystem.Content = Strings["PreflightDisableSystem"];
        CancelButton.Content = Strings["Cancel"]; StartButton.Content = Strings["PreflightStart"];
        ResetModel();
        SystemVolume.ValueChanged += OnVolumeChanged;
        MicrophoneVolume.ValueChanged += OnVolumeChanged;
        if (preparationOnly) SetPreparationState(RecordingPreparationState.Preparing);
        Opened += (_, _) => { if (!preparationOnly) _poll = PollAsync(begin: true, _pollCancellation.Token); };
    }
    private void ResetModel()
    {
        _appliedSystemVolume = _configuration.SystemAudioVolumePercent;
        _appliedMicrophoneVolume = _configuration.MicrophoneVolumePercent;
        Request = new(Guid.NewGuid()); _model = new(Request.OperationId); DataContext = _model;
        Render();
    }
    private async Task<PreflightReply> SendAsync(string command, PreflightRequest request, CancellationToken ct)
    {
        var response = await _send(command, request, ct);
        if (!response.Success) throw new IOException($"Preflight {command} was not acknowledged: {response.ErrorMessage}");
        var reply = JsonSerializer.Deserialize<PreflightReply>(response.DataJson!) ?? throw new InvalidDataException();
        if (!_model.Apply(reply))
        {
            if (reply.Status.OperationId == Request.OperationId && reply.ServerInstanceId == Request.ServerInstanceId) return reply;
            throw new InvalidDataException("Preflight identity changed.");
        }
        Request = Request with { ServerInstanceId = reply.ServerInstanceId, ConfigurationRevision = reply.Status.ConfigurationRevision };
        Render(); return reply;
    }
    private async Task PollAsync(bool begin, CancellationToken ct)
    {
        try
        {
            if (begin) await SendAsync("BeginPreflight", Request with { Configuration = _configuration }, ct);
            while (!ct.IsCancellationRequested)
            {
                await SendAsync("GetPreflightStatus", Request, ct);
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { if (!ct.IsCancellationRequested) Failure(); }
    }
    private void Render()
    {
        var status = _model.Status;
        _model.AudioBalancePending = _volumePending;
        SilenceNotice.Text = _model.SilenceNoticeKey is { } notice ? Strings[notice] : "";
        var microphoneEnabled = _configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        var systemEnabled = _configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        MicrophoneLabel.Text = Strings[microphoneEnabled ? "PreflightMicrophone" : "PreflightMicrophoneDisabled"];
        SystemLabel.Text = Strings[systemEnabled ? "PreflightSystem" : "PreflightSystemDisabled"];
        MicrophoneStatus.Text = Strings[microphoneEnabled ? RecordingPreflightViewModel.AudioStatusKey(status.Phase, status.Microphone.State) : "PreflightAudioNotRecorded"];
        SystemStatus.Text = Strings[systemEnabled ? RecordingPreflightViewModel.AudioStatusKey(status.Phase, status.SystemAudio.State) : "PreflightAudioNotRecorded"];
        var monitoring = status.Phase is PreflightPhase.Preparing or PreflightPhase.Checking or PreflightPhase.TrialRecording;
        MicrophoneMeter.IsVisible = microphoneEnabled && monitoring;
        SystemMeter.IsVisible = systemEnabled && monitoring;
        // The server meters already include the applied gain. While dragging,
        // show an estimate for the requested attenuation; never multiply twice.
        static double Estimated(double rms, int requested, int applied) => applied == 0 ? 0 : rms * requested / applied;
        MicrophoneMeter.Value = microphoneEnabled ? Estimated(status.Microphone.Rms, _configuration.MicrophoneVolumePercent, _appliedMicrophoneVolume) : 0;
        SystemMeter.Value = systemEnabled ? Estimated(status.SystemAudio.Rms, _configuration.SystemAudioVolumePercent, _appliedSystemVolume) : 0;
        var adjustable = !_model.Busy && !_closing && PreparationState is null &&
            status.Phase is PreflightPhase.Checking or PreflightPhase.Reviewing;
        SystemVolume.IsEnabled = systemEnabled && adjustable;
        MicrophoneVolume.IsEnabled = microphoneEnabled && adjustable;
        ResetVolumesButton.IsEnabled = adjustable;
        SystemVolumeValue.Text = $"{_configuration.SystemAudioVolumePercent}%";
        MicrophoneVolumeValue.Text = $"{_configuration.MicrophoneVolumePercent}%";
        if (_configuration.SystemAudioVolumePercent == 0 && systemEnabled && status.ErrorCode is null && status.SystemAudio.State != PreflightSourceState.Unavailable) SystemStatus.Text = Strings["PreflightVolumeZero"];
        if (_configuration.MicrophoneVolumePercent == 0 && microphoneEnabled && status.ErrorCode is null && status.Microphone.State != PreflightSourceState.Unavailable) MicrophoneStatus.Text = Strings["PreflightVolumeZero"];
        var mix = !_volumePending && status.MixLevel is { } measured && DateTimeOffset.UtcNow - measured.CapturedAt <= TimeSpan.FromSeconds(1)
            ? measured : (AudioLevelSample?)null;
        MixMeter.Value = mix?.Rms ?? 0;
        var overload = mix is { Peak: >= .98 };
        MixMeter.Foreground = overload ? Brushes.OrangeRed : Brush.Parse("#2563EB");
        MixStatus.Text = Strings[_volumePending ? "PreflightVolumeUpdating" : overload ? "PreflightMixHigh" :
            mix is null ? "PreflightMixWaiting" : "PreflightMixLive"];
        VolumeNotice.Text = _volumeChanged && (_volumePending || !status.HasTrial) ? Strings["PreflightVolumeChanged"] : "";
        DisableMicrophone.IsVisible = microphoneEnabled;
        DisableSystem.IsVisible = systemEnabled;
        TrialProgress.IsVisible = status.Phase == PreflightPhase.TrialRecording;
        TrialProgress.Value = status.TrialSeconds;
        StopReplayButton.IsEnabled = status.Playing && !_model.Busy;
        RecheckButton.IsEnabled = !_model.Busy && !_volumePending && status.Phase != PreflightPhase.Stopping;
        DisableMicrophone.IsEnabled = !_model.Busy && _configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        DisableSystem.IsEnabled = !_model.Busy && _configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        ErrorLabel.IsVisible = status.ErrorCode is not null || _model.ConnectionFailed;
        if (status.ErrorCode is not null) ErrorLabel.Text = Strings["PreflightFailed"];
        if (_model.ConnectionFailed) ErrorLabel.Text = Strings["PreflightConnectionError"];
        RenderPlaceholder();
    }
    private void RenderPlaceholder()
    {
        if (PreparationState is { } preparation)
        {
            StatusMessage.Text = Strings[preparation == RecordingPreparationState.Failed ? "RecordingPreparationFailed" :
                preparation is RecordingPreparationState.Confirming or RecordingPreparationState.Unconfirmed
                    ? "RecordingConfirming" : "RecordingPreparing"];
            StatusHint.Text = preparation == RecordingPreparationState.Failed ? "" : Strings[
                preparation == RecordingPreparationState.Unconfirmed ? "RecordingUnconfirmedHint" : "PreflightStartWait"];
            StatusActivity.IsVisible = preparation != RecordingPreparationState.Failed;
            return;
        }
        var status = _model.Status;
        var failed = _model.ConnectionFailed || status.ErrorCode is not null || status.Phase == PreflightPhase.Failed;
        var trialStarting = status.Phase == PreflightPhase.TrialStarting ||
            _pendingAction == "RecordPreflightTrial" && status.Phase is PreflightPhase.Checking or PreflightPhase.Reviewing;
        var trial = trialStarting || status.Phase is PreflightPhase.TrialRecording or PreflightPhase.TrialFinalizing;
        var stopping = _closing || status.Phase is PreflightPhase.Stopping or PreflightPhase.Closed;
        var reviewing = status.Phase == PreflightPhase.Reviewing && !status.Playing &&
            _pendingAction is not ("PlayPreflightTrial" or "RecordPreflightTrial");
        StatusActivity.IsVisible = !failed && !reviewing &&
            (stopping || trialStarting || status.Phase is PreflightPhase.Preparing or PreflightPhase.TrialFinalizing);
        StatusMessage.Text = Strings[_model.ConnectionFailed ? "PreflightConnectionError" : failed ? "PreflightFailed" : stopping ? "PreflightStopping" :
            trialStarting ? "PreflightTrialStarting" : status.Phase == PreflightPhase.TrialFinalizing ? "PreflightTrialFinalizing" :
            trial ? "PreflightTrialRecording" : reviewing ? "PreflightTrialReady" : status.Playing ? "PreflightPlaying" :
            status.Phase == PreflightPhase.Checking ? "PreflightChecking" : "PreflightPreparing"];
        if (!failed && !stopping && status.Phase == PreflightPhase.TrialRecording)
            StatusMessage.Text = Strings.GetFormatted("PreflightTrialRecording", Math.Clamp((int)Math.Ceiling(5 - status.TrialSeconds), 0, 5));
        StatusHint.Text = Strings[_model.ConnectionFailed ? "PreflightStartWait" : failed ? "PreflightAudioRetry" : stopping ? "PreflightStartWait" :
            trialStarting ? "PreflightTrialStartingHint" : trial ? "PreflightAudioTrialHint" : reviewing ? "PreflightTrialReadyHint" : "PreflightAudioHint"];
    }
    private void Failure()
    { _model.ConnectionFailed = true;
        ErrorLabel.IsVisible = true; ErrorLabel.Text = Strings["PreflightConnectionError"]; RecheckButton.IsEnabled = true; RenderPlaceholder(); }
    private async Task ActionAsync(string command)
    {
        if (!await _action.WaitAsync(0)) return;
        try { _model.Busy = true; _pendingAction = command; Render(); await SendAsync(command, Request, _pollCancellation.Token); }
        catch { Failure(); }
        finally { _pendingAction = null; _model.Busy = false; _action.Release(); Render(); }
    }
    private async void OnTrial(object? sender, RoutedEventArgs e) { if (_model.CanTrial) await ActionAsync("RecordPreflightTrial"); }
    private async void OnReplay(object? sender, RoutedEventArgs e) { if (_model.CanReplay) await ActionAsync("PlayPreflightTrial"); }
    private async void OnStopReplay(object? sender, RoutedEventArgs e) => await ActionAsync("StopPreflightReplay");
    private async Task StopPollingAsync()
    { _pollCancellation.Cancel(); await _poll; }
    private async Task EndAsync(bool confirm)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (Request.ServerInstanceId is null)
        {
            await SendAsync("GetPreflightStatus", Request, deadline.Token);
            if (_model.Status.Phase == PreflightPhase.Closed) return;
        }
        await SendAsync("EndPreflight", Request with { Confirm = confirm }, deadline.Token);
        while (_model.Status.Phase != PreflightPhase.Closed)
        {
            await Task.Delay(100, deadline.Token);
            await SendAsync("GetPreflightStatus", Request, deadline.Token);
        }
    }
    private async Task RecheckAsync(AudioSourceType? audio = null, bool waitForAction = false)
    {
        if (_closing) return;
        if (waitForAction) await _action.WaitAsync();
        else if (!await _action.WaitAsync(0)) return;
        try
        {
            if (_closing) return;
            _model.Busy = true; Render(); await StopPollingAsync(); await EndAsync(false);
            if (_closing) return;
            if (audio is { } source) _configuration.AudioSource = source;
            _pollCancellation.Dispose(); _pollCancellation = new(); ResetModel();
            _poll = PollAsync(true, _pollCancellation.Token);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Cannot restart preflight audio check"); Failure(); }
        finally { _model.Busy = false; _action.Release(); Render(); }
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVolumes) return;
        if (_closing || PreparationState is not null || _model.Busy ||
            _model.Status.Phase is not (PreflightPhase.Checking or PreflightPhase.Reviewing))
        {
            _syncingVolumes = true;
            try { SystemVolume.Value = _configuration.SystemAudioVolumePercent; MicrophoneVolume.Value = _configuration.MicrophoneVolumePercent; }
            finally { _syncingVolumes = false; }
            return;
        }
        _configuration.SystemAudioVolumePercent = (int)Math.Round(SystemVolume.Value);
        _configuration.MicrophoneVolumePercent = (int)Math.Round(MicrophoneVolume.Value);
        _volumePending = _volumeChanged = true;
        _volumeDebounce.Cancel(); _volumeDebounce.Dispose(); _volumeDebounce = new();
        var cancellation = _volumeDebounce.Token;
        _volumeUpdate = UpdateVolumesAsync(cancellation);
        Render();
    }

    private async Task UpdateVolumesAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            // Reuse the acknowledged stop/start lifecycle. A fresh operation
            // invalidates both the old trial and its cached confirmation.
            await RecheckAsync(waitForAction: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Cannot apply preflight audio balance"); Failure(); }
        finally
        {
            if (!ct.IsCancellationRequested) { _volumePending = false; Render(); }
        }
    }

    private void OnResetVolumes(object? sender, RoutedEventArgs e)
    { SystemVolume.Value = 100; MicrophoneVolume.Value = 100; }
    private async void OnRecheck(object? sender, RoutedEventArgs e) => await RecheckAsync();
    private async void OnDisableMicrophone(object? sender, RoutedEventArgs e)
    {
        await RecheckAsync(_configuration.AudioSource == AudioSourceType.SystemAndMicrophone ? AudioSourceType.SystemOnly : AudioSourceType.None);
    }
    private async void OnDisableSystem(object? sender, RoutedEventArgs e)
    {
        await RecheckAsync(_configuration.AudioSource == AudioSourceType.SystemAndMicrophone ? AudioSourceType.MicrophoneOnly : AudioSourceType.None);
    }
    private async Task FinishAsync(bool confirm)
    {
        if (PreparationState is { } preparing)
        {
            if (preparing == RecordingPreparationState.Failed) { _authorized = true; Close(); }
            return;
        }
        if (_closing)
        {
            if (!confirm) _cancelHandoff = true;
            return;
        }
        if (confirm && !_model.CanStart) return;
        _closing = true; _model.Busy = true; RenderPlaceholder();
        _volumeDebounce.Cancel();
        await _volumeUpdate;
        await _action.WaitAsync();
        try
        {
            await StopPollingAsync(); await EndAsync(confirm);
            if (confirm && !_cancelHandoff)
            {
                SetPreparationState(RecordingPreparationState.Preparing);
                _confirmation.TrySetResult(_configuration);
            }
            else
            {
                _confirmation.TrySetResult(null);
                _authorized = true;
                Close();
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Cannot complete preflight handoff"); _closing = false; Failure(); }
        finally { if (PreparationState is null) _model.Busy = false; _action.Release(); Render(); }
    }
    private async void OnStart(object? sender, RoutedEventArgs e) => await FinishAsync(true);
    private async void OnCancel(object? sender, RoutedEventArgs e) => await FinishAsync(false);
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (PreparationState == RecordingPreparationState.Failed) _authorized = true;
        if (!_authorized) { e.Cancel = true; _ = FinishAsync(false); }
        base.OnClosing(e);
    }

    public void SetPreparationState(RecordingPreparationState state)
    {
        PreparationState = state;
        if (state == RecordingPreparationState.Started)
        {
            _authorized = true; Close(); return;
        }
        _model.Busy = true;
        AudioCards.IsVisible = AudioActions.IsVisible = StartButton.IsVisible = false;
        MixArea.IsVisible = false;
        SilenceArea.IsVisible = false;
        TrialProgress.IsVisible = ErrorLabel.IsVisible = false;
        CancelButton.IsEnabled = state == RecordingPreparationState.Failed;
        CancelButton.Content = Strings[state == RecordingPreparationState.Failed ? "Close" : "Cancel"];
        Heading.Text = Title = Strings[state == RecordingPreparationState.Failed
            ? "RecordingPreparationFailedTitle" : "RecordingPreparing"];
        Privacy.IsVisible = false;
        MinHeight = 240; Height = 280;
        RenderPlaceholder();
    }

    protected override void OnClosed(EventArgs e)
    {
        _confirmation.TrySetResult(null);
        _pollCancellation.Cancel();
        _pollCancellation.Dispose();
        _volumeDebounce.Cancel(); _volumeDebounce.Dispose();
        base.OnClosed(e);
    }
}
