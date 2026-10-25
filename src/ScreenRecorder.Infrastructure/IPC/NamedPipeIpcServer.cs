// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Serilog;

namespace ScreenRecorder.Infrastructure.IPC;

public class NamedPipeIpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly byte[] _key;
    private readonly int _clientPid;
    private readonly bool _mediaResponse;
    private readonly SemaphoreSlim _connectionSlots = new(16, 16);
    private readonly Func<IpcMessage, Task<IpcResponse>> _messageHandler;
    private readonly Func<IpcMessage, Task<ProjectReply>>? _mediaHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private NamedPipeServerStream? _currentServerStream;
    private readonly HashSet<Task> _connectionTasks = new();
    private readonly SemaphoreSlim _messageGate = new(1, 1);
    private readonly object _lock = new();

    public event EventHandler<bool>? ClientConnectionChanged;

    public NamedPipeIpcServer(string pipeName, byte[] key, Func<IpcMessage, Task<ProjectReply>> mediaHandler, int? clientPid = null)
        : this(pipeName, key, _ => Task.FromResult(new IpcResponse()), clientPid)
    { _mediaHandler = mediaHandler; }

    public NamedPipeIpcServer(string pipeName, byte[] key, Func<IpcMessage, Task<IpcResponse>> messageHandler, int? clientPid = null, bool mediaResponse = false)
    {
        _pipeName = pipeName;
        _mediaResponse = mediaResponse;
        _key = AuthenticatedIpc.CopyKey(key);
        _clientPid = clientPid ?? Environment.ProcessId;
        _messageHandler = messageHandler;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        NamedPipeServerStream? acceptingServer = null;
        Task? acceptTask = null;

        try
        {
            acceptingServer = CreateServer();
            SetAcceptingServer(acceptingServer);
            acceptTask = acceptingServer.WaitForConnectionAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await acceptTask;

                var connectedServer = acceptingServer;

                // Start accepting the next client before replying to the
                // current one. Without this overlap, a back-to-back command
                // can connect while the previous server instance is closing
                // and receive an empty response / broken pipe on Unix.
                acceptingServer = CreateServer();
                SetAcceptingServer(acceptingServer);
                acceptTask = acceptingServer.WaitForConnectionAsync(
                    cancellationToken);

                if (!_connectionSlots.Wait(0))
                {
                    connectedServer.Dispose();
                    continue;
                }
                TrackConnection(
                    HandleConnectionAsync(
                        connectedServer,
                        cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Log.Error(ex, "IPC listener failed for pipe {PipeName}", _pipeName);
            }
        }
        finally
        {
            acceptingServer?.Dispose();
            SetAcceptingServer(null);

            Task[] activeConnections;
            lock (_lock)
            {
                activeConnections = _connectionTasks.ToArray();
            }

            try
            {
                await Task.WhenAll(activeConnections);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private NamedPipeServerStream CreateServer() =>
        new(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private void SetAcceptingServer(NamedPipeServerStream? server)
    {
        lock (_lock)
        {
            _currentServerStream = server;
        }
    }

    private void TrackConnection(Task connectionTask)
    {
        lock (_lock)
        {
            _connectionTasks.Add(connectionTask);
        }

        _ = connectionTask.ContinueWith(
            completedTask =>
            {
                lock (_lock)
                {
                    _connectionTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipeServer,
        CancellationToken cancellationToken)
    {
        using (pipeServer)
        {
            var enteredMessageGate = false;
            var stage = "authentication";
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N");
            var command = "unauthenticated";
            long gateWaitMs = 0, handlerMs = 0;
            try
            {
                // Incomplete or unauthenticated peers must never own the command gate.
                IpcPeerIdentity.Verify(pipeServer, _clientPid, server: true);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var nonce = await AuthenticatedIpc.SendChallengeAsync(pipeServer, _key, deadline.Token);
                var message = await AuthenticatedIpc.ReadAsync<IpcMessage>(pipeServer, _key, nonce, false, deadline.Token);
                if (message.RequestId != Guid.Empty) requestId = message.RequestId.ToString("N");
                command = message.MessageType.Length <= 80 && message.MessageType.All(c => char.IsAsciiLetterOrDigit(c))
                    ? message.MessageType : "invalid";
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                stage = "command-gate";
                var gateClock = System.Diagnostics.Stopwatch.StartNew();
                // Polls are disposable snapshots. Do not queue them behind a long
                // lifecycle command until their client has already timed out.
                if (_mediaHandler is null && command is "GetTelemetry" or "GetAudioLevels")
                {
                    if (!await _messageGate.WaitAsync(0, cancellationToken))
                    {
                        stage = "busy-response";
                        deadline.CancelAfter(TimeSpan.FromSeconds(2));
                        await AuthenticatedIpc.WriteAsync(pipeServer, _key, nonce, true,
                            new IpcResponse { Success = false, ErrorMessage = "RecorderBusy" }, deadline.Token, _mediaResponse);
                        return;
                    }
                }
                else await _messageGate.WaitAsync(cancellationToken);
                gateWaitMs = gateClock.ElapsedMilliseconds;
                enteredMessageGate = true;
                ClientConnectionChanged?.Invoke(this, true);
                var handlerClock = System.Diagnostics.Stopwatch.StartNew();

                if (_mediaHandler is not null)
                {
                    stage = "media-handler";
                    var response = await _mediaHandler(message);
                    handlerMs = handlerClock.ElapsedMilliseconds;
                    stage = "media-response";
                    deadline.CancelAfter(AuthenticatedMediaIpc.ResponseBudget(response.Frame?.Rgba?.Length ?? response.SubtitleData?.Length ?? 0));
                    await AuthenticatedMediaIpc.WriteAsync(pipeServer, _key, nonce, response, deadline.Token);
                }
                else
                {
                    stage = "command-handler";
                    var response = await _messageHandler(message);
                    handlerMs = handlerClock.ElapsedMilliseconds;
                    stage = "command-response";
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    await AuthenticatedIpc.WriteAsync(pipeServer, _key, nonce, true, response, deadline.Token, _mediaResponse);
                }
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    Log.Warning("IPC connection canceled at {Stage} after {ElapsedMs} ms", stage, elapsed.ElapsedMilliseconds);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Error(
                        ex,
                        "IPC connection failed: Request={RequestId}, Command={Command}, Stage={Stage}, GateWaitMs={GateWaitMs}, HandlerMs={HandlerMs}, ElapsedMs={ElapsedMs}",
                        requestId, command, stage, gateWaitMs, handlerMs, elapsed.ElapsedMilliseconds);
                }
            }
            finally
            {
                Log.Debug("IPC request completed: Request={RequestId}, Command={Command}, Stage={Stage}, GateWaitMs={GateWaitMs}, HandlerMs={HandlerMs}, ElapsedMs={ElapsedMs}",
                    requestId, command, stage, gateWaitMs, handlerMs, elapsed.ElapsedMilliseconds);
                _connectionSlots.Release();
                ClientConnectionChanged?.Invoke(this, false);
                if (enteredMessageGate)
                {
                    _messageGate.Release();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            lock (_lock)
            {
                try
                {
                    _currentServerStream?.Dispose();
                }
                catch { }
            }

            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask;
                }
                catch { }
            }
            _cts.Dispose();
            _messageGate.Dispose();
            _connectionSlots.Dispose();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_key);
        }
    }
}
