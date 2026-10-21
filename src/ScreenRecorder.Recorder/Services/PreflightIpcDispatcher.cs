// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenRecorder.Core.Preflight;
namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightIpcDispatcher : IAsyncDisposable
{
    private readonly PreflightCoordinator coordinator;
    private readonly Func<ScreenRecorder.Core.Models.RecordingConfiguration, string> _stamp;
    private ScreenRecorder.Core.Models.RecordingConfiguration? _checkingConfiguration;
    private string? _confirmed;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _expiry;
    public PreflightIpcDispatcher(PreflightCoordinator coordinator, Func<ScreenRecorder.Core.Models.RecordingConfiguration, string>? stamp = null)
    { this.coordinator = coordinator; _stamp = stamp ?? (configuration => JsonSerializer.Serialize(configuration)); _expiry = ExpireAsync(); }
    private Task _work = Task.CompletedTask;
    private Guid? _operation;
    private long _lastSeen = Environment.TickCount64;
    private string? _beginFingerprint, _failure;
    private PreflightPhase _queuedPhase = PreflightPhase.Preparing;
    private long _generation, _sequence;
    private IpcResponse Reply(PreflightStatus status, bool confirmed = false) => new() { Success = true,
        DataJson = JsonSerializer.Serialize(new PreflightReply(_instance, status, confirmed, Interlocked.Increment(ref _sequence))) };
    public static bool IsCommand(string type) => type is "BeginPreflight" or "GetPreflightStatus" or "RecordPreflightTrial"
        or "PlayPreflightTrial" or "StopPreflightReplay" or "EndPreflight" or "CheckPreflightConfirmation";
    private PreflightRequest Parse(IpcMessage message)
    {
        if (message.PayloadJson.Length > 65536) throw new InvalidDataException("Preflight request too large.");
        var request = JsonSerializer.Deserialize<PreflightRequest>(message.PayloadJson, new JsonSerializerOptions {
            MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing request.");
        if (request.OperationId == Guid.Empty ||
            (message.MessageType != "BeginPreflight" && !(message.MessageType == "GetPreflightStatus" && request.ServerInstanceId is null) && request.ServerInstanceId != _instance) ||
            (request.ServerInstanceId is { } instance && instance != _instance)) throw new InvalidOperationException("Preflight identity changed.");
        return request;
    }
    private PreflightStatus Status()
    {
        var status = coordinator.ReadCurrentStatus();
        var phase = !_work.IsCompleted ? _queuedPhase : status.Phase;
        if (!_work.IsCompleted && _queuedPhase == PreflightPhase.TrialStarting &&
            status.Phase is PreflightPhase.TrialStarting or PreflightPhase.TrialRecording or PreflightPhase.TrialFinalizing)
            phase = status.Phase;
        return status with { OperationId = _operation ?? Guid.Empty,
            Phase = _failure is not null ? PreflightPhase.Failed : phase,
            ErrorCode = _failure ?? status.ErrorCode };
    }
    public async Task<IpcResponse> DispatchAsync(IpcMessage message)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsCommand(message.MessageType)) throw new InvalidDataException("Unknown preflight command.");
            var request = Parse(message);
            if (message.MessageType == "GetPreflightStatus" && _operation is null)
                return Reply(PreflightStatus.Empty with { OperationId = request.OperationId });
            if (message.MessageType == "CheckPreflightConfirmation")
                return Reply(Status(),
                    _operation == request.OperationId && request.Configuration is not null && _confirmed is not null &&
                    _confirmed == _stamp(request.Configuration));
            if (message.MessageType == "BeginPreflight")
            {
                if (request.Configuration is null || request.Configuration.Fps is < 1 or > 120)
                    throw new InvalidDataException("Invalid preflight configuration.");
                var fingerprint = JsonSerializer.Serialize(request.Configuration);
                if (_operation == request.OperationId)
                {
                    if (fingerprint != _beginFingerprint) throw new InvalidOperationException("Operation reused with different settings.");
                }
                else
                {
                    if (!_work.IsCompleted || coordinator.CurrentOperation is not null) throw new InvalidOperationException("Preflight is busy.");
                    _operation = request.OperationId; _beginFingerprint = fingerprint; _failure = null; _confirmed = null;
                    _checkingConfiguration = request.Configuration;
                    _queuedPhase = PreflightPhase.Preparing;
                    _work = ObserveAsync(() => coordinator.BeginAsync(request.OperationId, request.Configuration, _lifetime.Token), defer: false);
                }
            }
            else
            {
                if (_operation != request.OperationId) throw new InvalidOperationException("Preflight identity changed.");
                if (coordinator.CurrentOperation is not null && request.ConfigurationRevision != coordinator.ReadCurrentStatus().ConfigurationRevision)
                    throw new InvalidOperationException("Preflight revision changed.");
                if (message.MessageType is not ("GetPreflightStatus" or "EndPreflight") && !_work.IsCompleted)
                    throw new InvalidOperationException("Preflight is busy.");
                switch (message.MessageType)
                {
                    case "RecordPreflightTrial":
                        _queuedPhase = PreflightPhase.TrialStarting;
                        _work = ObserveAsync(() => coordinator.RecordTrialAsync(request.OperationId, request.ConfigurationRevision, _lifetime.Token)); break;
                    case "PlayPreflightTrial":
                        _queuedPhase = PreflightPhase.Preparing;
                        _work = ObserveAsync(() => coordinator.SetReplayAsync(request.OperationId, request.ConfigurationRevision, true, _lifetime.Token)); break;
                    case "StopPreflightReplay":
                        _queuedPhase = PreflightPhase.Preparing;
                        _work = ObserveAsync(() => coordinator.SetReplayAsync(request.OperationId, request.ConfigurationRevision, false, _lifetime.Token)); break;
                    case "EndPreflight":
                        string? confirmed = null;
                        if (request.Confirm)
                        {
                            var status = Status();
                            if (status.Phase is not (PreflightPhase.Checking or PreflightPhase.Reviewing or PreflightPhase.Failed))
                                throw new InvalidOperationException("Preflight is busy.");
                            try { confirmed = _stamp(_checkingConfiguration!); }
                            catch (Exception ex)
                            {
                                // An unavailable endpoint prevents cached automatic resume
                                // approval, not the user's explicit request to record.
                                Serilog.Log.Warning(ex, "Cannot cache preflight device confirmation; explicit recording remains available");
                            }
                        }
                        _queuedPhase = PreflightPhase.Stopping;
                        _work = ObserveAsync(async () => { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            await coordinator.EndAsync(request.OperationId, stop.Token); _confirmed = confirmed; Volatile.Write(ref _failure, null); });
                        break;
                }
            }
            Interlocked.Exchange(ref _lastSeen, Environment.TickCount64);
            return Reply(Status());
        }
        catch (Exception ex) { return new() { Success = false, ErrorMessage = ex.Message }; }
        finally { _gate.Release(); }
    }
    private async Task ObserveAsync(Func<Task> action, bool defer = true)
    {
        var generation = Interlocked.Increment(ref _generation);
        var operation = _operation;
        try { if (defer) await Task.Yield(); await action(); }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Preflight operation failed: Phase={Phase}, Generation={Generation}", _queuedPhase, generation);
            if (generation == Volatile.Read(ref _generation) && operation == _operation)
                Volatile.Write(ref _failure, "PreflightOperationFailed");
        }
    }
    public Task<ProjectReply> DispatchMediaAsync(IpcMessage message)
    {
        var state = ProjectSnapshot.Closed with { ServerInstanceId = _instance };
        try
        {
            if (message.MessageType != "GetPreflightFrame") throw new InvalidDataException("Unknown preflight media command.");
            var request = Parse(message);
            return Task.FromResult(new ProjectReply(true, null, state) { Frame = coordinator.ReadFrame(request.OperationId, request.ConfigurationRevision) });
        }
        catch (Exception ex) { return Task.FromResult(new ProjectReply(false, ex.Message, state)); }
    }
    private async Task ExpireAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                if (Environment.TickCount64 - Interlocked.Read(ref _lastSeen) > 15000 && coordinator.CurrentOperation is { } id)
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await coordinator.EndAsync(id, stop.Token); }
                    catch { if (_operation == id) Volatile.Write(ref _failure, "PreflightStopUnconfirmed"); }
                }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _expiry;
        if (coordinator.CurrentOperation is { } id)
        { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await coordinator.EndAsync(id, stop.Token); }
        await _work;
        _lifetime.Dispose();
    }
}
