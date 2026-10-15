// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Preflight;

public partial class RecordingPreflightWindow : Window
{
    private readonly Func<string, PreflightRequest, CancellationToken, Task<IpcResponse>> _send;
    private readonly Func<PreflightRequest, CancellationToken, Task<ProjectReply>> _frame;
    private readonly RecordingConfiguration _configuration;
    private RecordingPreflightViewModel _model = null!;
    private CancellationTokenSource _pollCancellation = new();
    private Task _poll = Task.CompletedTask;
    private WriteableBitmap? _bitmap;
    private bool _closing, _authorized;
    private readonly SemaphoreSlim _action = new(1, 1);
    public PreflightRequest Request { get; private set; } = new(Guid.NewGuid());
    private static LanguageManager Strings => LanguageManager.Instance;
    public RecordingPreflightWindow(RecordingConfiguration configuration,
        Func<string, PreflightRequest, CancellationToken, Task<IpcResponse>> send,
        Func<PreflightRequest, CancellationToken, Task<ProjectReply>> frame)
    {
        _configuration = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(configuration))!;
        _send = send; _frame = frame;
        InitializeComponent();
        Title = Heading.Text = Strings["PreflightTitle"];
        Privacy.Text = Strings["PreflightPrivacy"];
        DisplayLabel.Text = Strings["PreflightDisplay"];
        MicrophoneLabel.Text = Strings["PreflightMicrophone"]; SystemLabel.Text = Strings["PreflightSystem"];
        ScreenAcknowledgementText.Text = Strings["PreflightScreenConfirm"];
        SilentAcknowledgementText.Text = Strings["PreflightSilenceAck"];
        TrialButton.Content = Strings["PreflightTrial"]; ReplayButton.Content = Strings["PreflightReplay"];
        StopReplayButton.Content = Strings["PreflightStopReplay"]; RecheckButton.Content = Strings["PreflightRecheck"];
        DisableMicrophone.Content = Strings["PreflightDisableMic"]; DisableSystem.Content = Strings["PreflightDisableSystem"];
        CancelButton.Content = Strings["Cancel"]; StartButton.Content = Strings["PreflightStart"];
        ResetModel();
        Opened += (_, _) => _poll = PollAsync(begin: true, _pollCancellation.Token);
    }
    private void ResetModel()
    {
        Request = new(Guid.NewGuid()); _model = new(Request.OperationId); DataContext = _model;
        Preview.Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
    private async Task<PreflightReply> SendAsync(string command, PreflightRequest request, CancellationToken ct)
    {
        var response = await _send(command, request, ct);
        if (!response.Success) throw new IOException("Preflight command was not acknowledged.");
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
                if (_model.Status.HasFrame && _model.Status.ErrorCode is null)
                {
                    var phase = _model.Status.Phase;
                    var reply = await _frame(Request, ct);
                    if (_model.Status.HasFrame && _model.Status.ErrorCode is null && _model.Status.Phase == phase &&
                        reply.Success && reply.State.ServerInstanceId == Request.ServerInstanceId && reply.Frame is { } frame &&
                        frame.ClipId == Request.OperationId && frame.Revision == Request.ConfigurationRevision &&
                        frame.PixelWidth is > 0 and <= 960 && frame.PixelHeight is > 0 and <= 540 &&
                        frame.Rgba?.Length == frame.PixelWidth * frame.PixelHeight * 4)
                    {
                        var bitmap = new WriteableBitmap(new(frame.PixelWidth, frame.PixelHeight), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
                        using (var buffer = bitmap.Lock())
                            for (var row = 0; row < frame.PixelHeight; row++)
                                Marshal.Copy(frame.Rgba, row * frame.PixelWidth * 4, buffer.Address + row * buffer.RowBytes, frame.PixelWidth * 4);
                        var previous = _bitmap; _bitmap = bitmap; Preview.Source = bitmap; previous?.Dispose();
                    }
                }
                await Task.Delay(250, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { if (!ct.IsCancellationRequested) Failure(); }
    }
    private void Render()
    {
        var status = _model.Status;
        if (!status.HasFrame || status.ErrorCode is not null)
        { Preview.Source = null; _bitmap?.Dispose(); _bitmap = null; }
        MicrophoneStatus.Text = Strings["PreflightSource" + status.Microphone.State];
        SystemStatus.Text = Strings["PreflightSource" + status.SystemAudio.State];
        MicrophoneMeter.Value = status.Microphone.State == PreflightSourceState.Live ? status.Microphone.Rms : 0;
        SystemMeter.Value = status.SystemAudio.State == PreflightSourceState.Live ? status.SystemAudio.Rms : 0;
        BoundsLabel.Text = $"{status.DisplayName} · {status.Bounds.Width} × {status.Bounds.Height} · ({status.Bounds.X}, {status.Bounds.Y})";
        PhaseLabel.Text = Strings["Preflight" + status.Phase];
        TrialProgress.IsVisible = status.Phase == PreflightPhase.TrialRecording;
        TrialProgress.Value = status.TrialSeconds;
        StopReplayButton.IsEnabled = status.Playing && !_model.Busy;
        RecheckButton.IsEnabled = !_model.Busy && status.Phase != PreflightPhase.Stopping;
        DisableMicrophone.IsEnabled = !_model.Busy && _configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        DisableSystem.IsEnabled = !_model.Busy && _configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        ErrorLabel.IsVisible = status.ErrorCode is not null || _model.ConnectionFailed;
        if (status.ErrorCode is not null) ErrorLabel.Text = Strings["PreflightFailed"];
        if (_model.ConnectionFailed) ErrorLabel.Text = Strings["PreflightConnectionError"];
    }
    private void Failure()
    { _model.ConnectionFailed = true; Preview.Source = null; _bitmap?.Dispose(); _bitmap = null;
        ErrorLabel.IsVisible = true; ErrorLabel.Text = Strings["PreflightConnectionError"]; RecheckButton.IsEnabled = true; }
    private async Task ActionAsync(string command)
    {
        if (!await _action.WaitAsync(0)) return;
        try { _model.Busy = true; await SendAsync(command, Request, _pollCancellation.Token); }
        catch { Failure(); }
        finally { _model.Busy = false; _action.Release(); Render(); }
    }
    private async void OnTrial(object? sender, RoutedEventArgs e) => await ActionAsync("RecordPreflightTrial");
    private async void OnReplay(object? sender, RoutedEventArgs e) => await ActionAsync("PlayPreflightTrial");
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
    private async Task RecheckAsync(AudioSourceType? audio = null)
    {
        if (_closing || !await _action.WaitAsync(0)) return;
        try
        {
            _model.Busy = true; await StopPollingAsync(); await EndAsync(false);
            if (_closing) return;
            if (audio is { } source) _configuration.AudioSource = source;
            _pollCancellation.Dispose(); _pollCancellation = new(); ResetModel();
            _poll = PollAsync(true, _pollCancellation.Token);
        }
        catch { Failure(); }
        finally { _action.Release(); }
    }
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
        if (_closing || confirm && !_model.CanStart) return;
        _closing = true; _model.Busy = true;
        await _action.WaitAsync();
        try
        {
            await StopPollingAsync(); await EndAsync(confirm);
            _authorized = true; _bitmap?.Dispose(); _bitmap = null;
            Close(confirm ? _configuration : null);
        }
        catch { _closing = false; Failure(); }
        finally { _action.Release(); }
    }
    private async void OnStart(object? sender, RoutedEventArgs e) => await FinishAsync(true);
    private async void OnCancel(object? sender, RoutedEventArgs e) => await FinishAsync(false);
    protected override void OnClosing(WindowClosingEventArgs e)
    { if (!_authorized) { e.Cancel = true; _ = FinishAsync(false); } base.OnClosing(e); }
}
