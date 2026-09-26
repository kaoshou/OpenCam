// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ScreenRecorder.Infrastructure.IPC;

public class NamedPipeIpcClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly byte[] _key;
    private readonly int _serverPid;

    public NamedPipeIpcClient(string pipeName, byte[] key, int? serverPid = null)
    {
        _pipeName = pipeName;
        _key = AuthenticatedIpc.CopyKey(key);
        _serverPid = serverPid ?? Environment.ProcessId;
    }

    public async Task<IpcResponse> SendCommandAsync(string messageType, object payload, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await pipeClient.ConnectAsync(linkedCts.Token);
            IpcPeerIdentity.Verify(pipeClient, _serverPid, server: false);

            var nonce = await AuthenticatedIpc.ReadChallengeAsync(pipeClient, _key, linkedCts.Token);

            var message = new IpcMessage
            {
                MessageType = messageType,
                PayloadJson = JsonSerializer.Serialize(payload)
            };

            await AuthenticatedIpc.WriteAsync(pipeClient, _key, nonce, false, message, linkedCts.Token);
            return await AuthenticatedIpc.ReadAsync<IpcResponse>(pipeClient, _key, nonce, true, linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new IpcResponse { Success = false, ErrorMessage = ex.Message, TimedOut = true };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public ValueTask DisposeAsync()
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(_key);
        return ValueTask.CompletedTask;
    }
}
