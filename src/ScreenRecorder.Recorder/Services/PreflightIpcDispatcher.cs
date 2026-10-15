// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenRecorder.Core.Preflight;
namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightIpcDispatcher : IAsyncDisposable
{
    private readonly PreflightCoordinator coordinator;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _expiry;
    public PreflightIpcDispatcher(PreflightCoordinator coordinator)
    { this.coordinator = coordinator; _expiry = ExpireAsync(); }
    private Task _work = Task.CompletedTask;
    private Guid? _operation;
    private long _lastSeen = Environment.TickCount64;
    private string? _beginFingerprint, _failure;
    public static bool IsCommand(string type) => type is "BeginPreflight" or "GetPreflightStatus" or "RecordPreflightTrial"
        or "PlayPreflightTrial" or "StopPreflightReplay" or "EndPreflight";
    private PreflightRequest Parse(IpcMessage message)
    {
        if (message.PayloadJson.Length > 65536) throw new InvalidDataException("Preflight request too large.");
        var request = JsonSerializer.Deserialize<PreflightRequest>(message.PayloadJson, new JsonSerializerOptions {
            MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing request.");
        if (request.OperationId == Guid.Empty ||
            (message.MessageType != "BeginPreflight" && request.ServerInstanceId != _instance) ||
            (request.ServerInstanceId is { } instance && instance != _instance)) throw new InvalidOperationException("Preflight identity changed.");
        return request;
    }
    private PreflightStatus Status()
    {
        var status = coordinator.ReadCurrentStatus();
        return status with { OperationId = _operation ?? Guid.Empty,
            Phase = _failure is not null ? PreflightPhase.Failed : status.Phase, ErrorCode = _failure ?? status.ErrorCode };
    }
    public async Task<IpcResponse> DispatchAsync(IpcMessage message)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsCommand(message.MessageType)) throw new InvalidDataException("Unknown preflight command.");
            var request = Parse(message);
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
                    _operation = request.OperationId; _beginFingerprint = fingerprint; _failure = null;
                    _work = ObserveAsync(() => coordinator.BeginAsync(request.OperationId, request.Configuration, _lifetime.Token));
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
                        _work = ObserveAsync(() => coordinator.RecordTrialAsync(request.OperationId, request.ConfigurationRevision, _lifetime.Token)); break;
                    case "PlayPreflightTrial":
                        await coordinator.SetReplayAsync(request.OperationId, request.ConfigurationRevision, true, _lifetime.Token); break;
                    case "StopPreflightReplay":
                        _work = ObserveAsync(() => coordinator.SetReplayAsync(request.OperationId, request.ConfigurationRevision, false, _lifetime.Token)); break;
                    case "EndPreflight":
                        if (request.Confirm)
                        {
                            var status = Status();
                            bool Readable(PreflightSourceResult source) => source.State is PreflightSourceState.Disabled or PreflightSourceState.Live or PreflightSourceState.Silent;
                            if (status.Phase != PreflightPhase.Checking || !status.HasFrame || status.ErrorCode is not null ||
                                !Readable(status.Microphone) || !Readable(status.SystemAudio)) throw new InvalidOperationException("Preflight must be checked again before recording.");
                        }
                        _work = ObserveAsync(async () => { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            await coordinator.EndAsync(request.OperationId, stop.Token); Volatile.Write(ref _failure, null); });
                        break;
                }
            }
            Interlocked.Exchange(ref _lastSeen, Environment.TickCount64);
            return new() { Success = true, DataJson = JsonSerializer.Serialize(new PreflightReply(_instance, Status())) };
        }
        catch (Exception ex) { return new() { Success = false, ErrorMessage = ex.Message }; }
        finally { _gate.Release(); }
    }
    private async Task ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception) { Volatile.Write(ref _failure, "PreflightOperationFailed"); }
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
                    catch { Volatile.Write(ref _failure, "PreflightStopUnconfirmed"); }
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
