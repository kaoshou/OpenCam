// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public interface IProjectClient
{
    Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default);
}

public sealed class ProjectClient(Func<string, ProjectRequest, CancellationToken, Task<IpcResponse>> send,
    Func<ProjectRequest, CancellationToken, Task<IpcResponse>>? sendFrame = null) : IProjectClient
{
    private Guid? _instanceId;
    public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
    {
        // Bind retries to one recorder lifetime: its operation cache is not durable.
        if (_instanceId is null)
        {
            var status = await SendOnceAsync("GetProjectStatus", new(), ct);
            if (status?.State.ServerInstanceId is not Guid instance)
                return new(false, "Recorder status is unconfirmed.", ProjectSnapshot.Closed, Unconfirmed: true, OperationKnown: false);
            _instanceId = instance;
        }
        request = request with { ServerInstanceId = _instanceId };
        var result = await SendOnceAsync(command, request, ct);
        if (result is not null) return result;
        // Never reissue a mutating command with a new ID after a lost response.
        if (request.OperationId != Guid.Empty && command != "GetProjectStatus")
        {
            result = await SendOnceAsync("GetProjectStatus", new() { OperationId = request.OperationId, ServerInstanceId = _instanceId }, ct);
            if (result?.OperationKnown == true) return result;
        }
        return new(false, "Recorder status is unconfirmed. Query status before continuing.",
            result?.State ?? ProjectSnapshot.Closed, Unconfirmed: true, OperationKnown: false);
    }

    private async Task<ProjectReply?> SendOnceAsync(string command, ProjectRequest request, CancellationToken ct)
    {
        try
        {
            var response = command == "GetProjectFrame"
                ? sendFrame is not null ? await sendFrame(request, ct) : new IpcResponse { Success = false }
                : await send(command, request, ct);
            return response.DataJson is null ? null : JsonSerializer.Deserialize<ProjectReply>(response.DataJson);
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { return null; }
    }
}
