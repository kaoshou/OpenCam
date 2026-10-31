// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Infrastructure.IPC;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenRecorder.Recorder.Services;

// Long permission interaction runs outside the IPC message gate. Begin, poll
// and cancel exchange only operation identity and a public source thumbnail.
public sealed class LinuxSourceSelectionService(RecordingCaptureLease lease, Func<bool> canSelect,
    Func<CancellationToken, Task<LinuxPortalSelectionView>> prepare) : IAsyncDisposable
{
    private readonly SemaphoreSlim beginGate = new(1,1);
    private readonly object gate = new();
    private readonly Guid instance = Guid.NewGuid();
    private LinuxSourceSelectionSnapshot? snapshot;
    private CancellationTokenSource? cancellation;
    private Task work = Task.CompletedTask;
    private bool disposed;
    public static bool IsCommand(string type) => type is "BeginLinuxSourceSelection" or "GetLinuxSourceSelection" or "CancelLinuxSourceSelection";

    public async Task<LinuxSourceSelectionSnapshot> BeginAsync(Guid id)
    {
        await beginGate.WaitAsync().ConfigureAwait(false);
        try {
            lock (gate) {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (id == Guid.Empty) throw new InvalidOperationException("Source selection identity is required.");
                if (!work.IsCompleted) {
                    if (snapshot?.OperationId == id) return snapshot;
                    throw new InvalidOperationException("Source selection is already active.");
                }
            }
            if (!canSelect()) throw new InvalidOperationException("Stop recording before selecting another source.");
            var ownership = await lease.AcquireAsync(id, CancellationToken.None).ConfigureAwait(false);
            lock (gate) {
                cancellation?.Dispose();
                cancellation = new();
                snapshot = new(instance,id,"selecting");
                var token = cancellation.Token;
                work = Task.Run(() => SelectAsync(id, ownership, token));
                return snapshot;
            }
        } finally { beginGate.Release(); }
    }
    private async Task SelectAsync(Guid id, IAsyncDisposable ownership, CancellationToken ct)
    {
        LinuxSourceSelectionSnapshot result;
        try {
            var selected = await prepare(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            result = new(instance,id,"ready",selected);
        } catch (OperationCanceledException) { result = new(instance,id,"cancelled"); }
        catch (Exception error) {
            Serilog.Log.Warning("Linux source selection failed: ErrorType={ErrorType}",error.GetType().Name);
            result = new(instance,id,"failed",ErrorCode:"portal_selection_failed");
        } finally { await ownership.DisposeAsync().ConfigureAwait(false); }
        lock (gate) snapshot = result;
    }
    public LinuxSourceSelectionSnapshot Read(Guid id, Guid? server)
    {
        lock (gate) return id!=Guid.Empty&&snapshot is { } value && value.OperationId==id && (server is null||instance==server)
            ? value : throw new InvalidOperationException("Source selection identity changed.");
    }
    public void Cancel(Guid id, Guid? server)
    {
        lock (gate) {
            _ = Read(id,server);
            cancellation?.Cancel();
        }
    }
    public async Task<IpcResponse> DispatchAsync(IpcMessage message)
    {
        if (!IsCommand(message.MessageType) || message.PayloadJson.Length > 4096) throw new InvalidDataException("Invalid source selection command.");
        var request = JsonSerializer.Deserialize<LinuxSourceSelectionRequest>(message.PayloadJson,new JsonSerializerOptions {
            MaxDepth=8, UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Source selection identity is required.");
        LinuxSourceSelectionSnapshot value;
        if (message.MessageType=="BeginLinuxSourceSelection") value=await BeginAsync(request.OperationId).ConfigureAwait(false);
        else {
            // The authenticated caller may lose Begin's reply; its unpredictable
            // operation id still scopes recovery. A known server id remains pinned.
            var server=request.ServerInstanceId;
            if (message.MessageType=="CancelLinuxSourceSelection") Cancel(request.OperationId,server);
            value=Read(request.OperationId,server);
        }
        return new() {Success=true,DataJson=JsonSerializer.Serialize(value)};
    }
    public async ValueTask DisposeAsync()
    {
        await beginGate.WaitAsync().ConfigureAwait(false);
        try {
            Task pending;
            lock (gate) { if (disposed) return; disposed=true; cancellation?.Cancel(); pending=work; }
            await pending.ConfigureAwait(false);
            cancellation?.Dispose();
        } finally { beginGate.Release(); }
    }
}
