// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ScreenRecorder.Infrastructure.IPC;

public class NamedPipeIpcClient : IAsyncDisposable
{
    private readonly string _pipeName;

    public NamedPipeIpcClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<IpcResponse> SendCommandAsync(string messageType, object payload, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await pipeClient.ConnectAsync(linkedCts.Token);

            using var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true);
            using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);

            var message = new IpcMessage
            {
                MessageType = messageType,
                PayloadJson = JsonSerializer.Serialize(payload)
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(message));
            await writer.FlushAsync(linkedCts.Token);

            var responseLine = await reader.ReadLineAsync(linkedCts.Token);

            if (string.IsNullOrEmpty(responseLine))
            {
                return new IpcResponse { Success = false, ErrorMessage = "空回應或管道中斷" };
            }

            return JsonSerializer.Deserialize<IpcResponse>(responseLine) 
                ?? new IpcResponse { Success = false, ErrorMessage = "無法解析回應" };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
