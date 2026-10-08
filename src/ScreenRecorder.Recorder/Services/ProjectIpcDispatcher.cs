// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Called only after the existing IPC authentication/size gate.</summary>
public sealed class ProjectIpcDispatcher(ProjectRecordingCoordinator coordinator)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, (string Fingerprint, bool Success, string? Error)> _operations = new();
    public static bool IsProjectCommand(string command) => command is "CreateProject" or "OpenProject" or "GetProjectStatus"
        or "GetProjectClips" or "StartProjectRecording" or "PauseProjectRecording" or "FinishProjectRecording"
        or "SaveProject" or "RenameProjectClip" or "UndoProject" or "RedoProject" or "CloseProject" or "RestoreProjectBackup";

    private ProjectSnapshot Snapshot() => new(coordinator.Current?.ProjectId, coordinator.Current?.Name ?? "",
        coordinator.ProjectDirectory, coordinator.Current?.Revision ?? 0, coordinator.SavedRevision, coordinator.Mode,
        coordinator.Current?.Clips.Length ?? 0, coordinator.CanUndo, coordinator.CanRedo, coordinator.LastError)
        { NeedsRecoveryConfirmation = coordinator.NeedsRecoveryConfirmation };

    public async Task<IpcResponse> DispatchAsync(IpcMessage message)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsProjectCommand(message.MessageType)) throw new InvalidDataException("Unknown project command.");
            if (message.PayloadJson.Length > 65536) throw new InvalidDataException("Project command is too large.");
            var request = JsonSerializer.Deserialize<ProjectRequest>(message.PayloadJson,
                new JsonSerializerOptions { MaxDepth = 16 }) ?? throw new InvalidDataException("Missing project request.");
            var fingerprint = message.MessageType + ":" + JsonSerializer.Serialize(request);
            if (message.MessageType == "GetProjectStatus")
            {
                if (request.OperationId != Guid.Empty && _operations.TryGetValue(request.OperationId, out var known))
                    return Reply(new(known.Success, known.Error, Snapshot()));
                return Reply(new(true, null, Snapshot(), OperationKnown: request.OperationId == Guid.Empty));
            }
            if (request.OperationId != Guid.Empty && _operations.TryGetValue(request.OperationId, out var old))
            {
                if (old.Fingerprint != fingerprint) throw new InvalidDataException("Operation ID reused with different content.");
                return Reply(new(old.Success, old.Error, Snapshot()));
            }
            if (message.MessageType is not ("CreateProject" or "OpenProject") &&
                (request.ProjectId is null || request.ProjectId != coordinator.Current?.ProjectId))
                throw new InvalidDataException("Project identity mismatch.");
            if (message.MessageType == "GetProjectClips")
            {
                if (request.Offset < 0 || request.Limit is < 1 or > 100) throw new InvalidDataException("Invalid clip page.");
                return Reply(new(true, null, Snapshot(), coordinator.Current!.Clips.Skip(request.Offset).Take(request.Limit).ToArray()));
            }
            if (request.OperationId == Guid.Empty) throw new InvalidDataException("An operation ID is required.");
            if (_operations.Count >= 20000) throw new InvalidOperationException("Save and restart OpenCam before issuing more project commands.");
            ProjectCommandResult result = message.MessageType switch
            {
                "CreateProject" => await coordinator.CreateAsync(request.Path ?? "", request.Name ?? ""),
                "OpenProject" => await coordinator.OpenAsync(request.Path ?? ""),
                "StartProjectRecording" => await coordinator.StartAsync(request.Configuration ?? throw new InvalidDataException("Missing capture configuration."), request.OperationId),
                "PauseProjectRecording" => await coordinator.PauseAsync(request.OperationId),
                "FinishProjectRecording" => await coordinator.FinishAsync(request.OperationId),
                "SaveProject" => await coordinator.SaveAsync(request.ExpectedRevision),
                "RenameProjectClip" => await coordinator.RenameClipAsync(request.ClipId, request.Name ?? "", request.ExpectedRevision),
                "UndoProject" => await coordinator.UndoAsync(request.ExpectedRevision),
                "RedoProject" => await coordinator.RedoAsync(request.ExpectedRevision),
                "CloseProject" => await coordinator.CloseAsync(),
                "RestoreProjectBackup" => await coordinator.RestoreBackupAsync(),
                _ => throw new InvalidDataException("Unknown command.")
            };
            _operations.Add(request.OperationId, (fingerprint, result.Success, result.ErrorCode));
            return Reply(new(result.Success, result.ErrorCode, Snapshot()));
        }
        catch (Exception ex) { return Reply(new(false, ex.Message, Snapshot())); }
        finally { _gate.Release(); }
    }

    private static IpcResponse Reply(ProjectReply reply) => new() {
        Success = reply.Success, ErrorMessage = reply.Error, DataJson = JsonSerializer.Serialize(reply) };
}
