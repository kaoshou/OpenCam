using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Serilog;

namespace ScreenRecorder.Infrastructure.IPC;

public class NamedPipeIpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<IpcMessage, Task<IpcResponse>> _messageHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private NamedPipeServerStream? _currentServerStream;
    private readonly object _lock = new();

    public event EventHandler<bool>? ClientConnectionChanged;

    public NamedPipeIpcServer(string pipeName, Func<IpcMessage, Task<IpcResponse>> messageHandler)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_lock)
                {
                    _currentServerStream = pipeServer;
                }

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                ClientConnectionChanged?.Invoke(this, true);

                using (var reader = new StreamReader(pipeServer, Encoding.UTF8, leaveOpen: true))
                using (var writer = new StreamWriter(pipeServer, Encoding.UTF8, leaveOpen: true))
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        var msg = JsonSerializer.Deserialize<IpcMessage>(line);
                        if (msg != null)
                        {
                            var response = await _messageHandler(msg);
                            var responseJson = JsonSerializer.Serialize(response);
                            await writer.WriteLineAsync(responseJson);
                            await writer.FlushAsync(cancellationToken);
                        }
                    }
                }

                if (pipeServer.IsConnected)
                {
                    pipeServer.Disconnect();
                }

                ClientConnectionChanged?.Invoke(this, false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;
                Log.Error(ex, "IPC listener failed for pipe {PipeName}", _pipeName);
                try
                {
                    await Task.Delay(50, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                lock (_lock)
                {
                    _currentServerStream = null;
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
        }
    }
}
