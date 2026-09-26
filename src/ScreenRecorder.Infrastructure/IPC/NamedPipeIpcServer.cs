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
    private readonly SemaphoreSlim _connectionSlots = new(16, 16);
    private readonly Func<IpcMessage, Task<IpcResponse>> _messageHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private NamedPipeServerStream? _currentServerStream;
    private readonly HashSet<Task> _connectionTasks = new();
    private readonly SemaphoreSlim _messageGate = new(1, 1);
    private readonly object _lock = new();

    public event EventHandler<bool>? ClientConnectionChanged;

    public NamedPipeIpcServer(string pipeName, byte[] key, Func<IpcMessage, Task<IpcResponse>> messageHandler, int? clientPid = null)
    {
        _pipeName = pipeName;
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
            try
            {
                // Incomplete or unauthenticated peers must never own the command gate.
                IpcPeerIdentity.Verify(pipeServer, _clientPid, server: true);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var nonce = await AuthenticatedIpc.SendChallengeAsync(pipeServer, _key, deadline.Token);
                var message = await AuthenticatedIpc.ReadAsync<IpcMessage>(pipeServer, _key, nonce, false, deadline.Token);
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                await _messageGate.WaitAsync(cancellationToken);
                enteredMessageGate = true;
                ClientConnectionChanged?.Invoke(this, true);

                var response = await _messageHandler(message);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await AuthenticatedIpc.WriteAsync(pipeServer, _key, nonce, true, response, deadline.Token);
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
                    Log.Error(
                        ex,
                        "IPC connection failed for pipe {PipeName}",
                        _pipeName);
                }
            }
            finally
            {
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
