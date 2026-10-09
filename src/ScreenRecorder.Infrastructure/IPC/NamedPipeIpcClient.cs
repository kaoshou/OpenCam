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
    private readonly bool _mediaResponse;

    public NamedPipeIpcClient(string pipeName, byte[] key, int? serverPid = null, bool mediaResponse = false)
    {
        _pipeName = pipeName;
        _mediaResponse = mediaResponse;
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
            return await AuthenticatedIpc.ReadAsync<IpcResponse>(pipeClient, _key, nonce, true, linkedCts.Token, _mediaResponse);
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

    public async Task<ProjectReply> SendProjectFrameAsync(ProjectRequest request, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName + "-frames", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await pipe.ConnectAsync(deadline.Token);
            IpcPeerIdentity.Verify(pipe, _serverPid, server: false);
            var nonce = await AuthenticatedIpc.ReadChallengeAsync(pipe, _key, deadline.Token);
            await AuthenticatedIpc.WriteAsync(pipe, _key, nonce, false, new IpcMessage {
                MessageType = "GetProjectFrame", PayloadJson = JsonSerializer.Serialize(request) }, deadline.Token);
            return await AuthenticatedMediaIpc.ReadAsync(pipe, _key, nonce, deadline.Token);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or JsonException)
        { return new(false, ex.Message, ProjectSnapshot.Closed, Unconfirmed: true); }
    }

    public ValueTask DisposeAsync()
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(_key);
        return ValueTask.CompletedTask;
    }
}
