// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Called only after the existing IPC authentication/size gate.</summary>
public sealed class ProjectIpcDispatcher(ProjectRecordingCoordinator coordinator, ProjectWaveformService? waveforms = null,
    ProjectFrameService? frames = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Dictionary<Guid, (string Fingerprint, bool Success, string? Error)> _operations = new();
    public static bool IsProjectCommand(string command) => command is "CreateProject" or "OpenProject" or "GetProjectStatus"
        or "GetProjectClips" or "GetProjectWaveform" or "StartProjectRecording" or "PauseProjectRecording" or "FinishProjectRecording"
        or "SaveProject" or "RenameProjectClip" or "ApplyProjectEdit" or "UndoProject" or "RedoProject" or "CloseProject" or "RestoreProjectBackup"
        or "StartNewRecordingContent" or "FinishRecordingContent" or "RetryRecordingContentExport" or "CancelRecordingContentExport";

    private ProjectSnapshot Snapshot() => new(coordinator.Current?.ProjectId, coordinator.Current?.Name ?? "",
        coordinator.ProjectDirectory, coordinator.Current?.Revision ?? 0, coordinator.SavedRevision, coordinator.Mode,
        coordinator.Current?.Clips.Length ?? 0, coordinator.CanUndo, coordinator.CanRedo, coordinator.LastError)
        { NeedsRecoveryConfirmation = coordinator.NeedsRecoveryConfirmation, ServerInstanceId = _instanceId, Export = coordinator.ExportStatus };

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
            if (request.ServerInstanceId is Guid instance && instance != _instanceId)
                throw new InvalidOperationException("Recorder restarted; reopen the workspace before continuing. The previous command was not replayed.");
            if (message.MessageType == "GetProjectStatus")
            {
                await coordinator.ReconcileRecorderStatusAsync();
                if (request.OperationId != Guid.Empty && _operations.TryGetValue(request.OperationId, out var known))
                    return Reply(new(known.Success, known.Error, Snapshot()));
                return Reply(new(true, null, Snapshot(), OperationKnown: request.OperationId == Guid.Empty));
            }
            if (request.OperationId != Guid.Empty && _operations.TryGetValue(request.OperationId, out var old))
            {
                if (old.Fingerprint != fingerprint) throw new InvalidDataException("Operation ID reused with different content.");
                return Reply(new(old.Success, old.Error, Snapshot()));
            }
            if (message.MessageType is not ("CreateProject" or "OpenProject" or "StartNewRecordingContent") &&
                (request.ProjectId is null || request.ProjectId != coordinator.Current?.ProjectId))
                throw new InvalidDataException("Project identity mismatch.");
            if (message.MessageType == "GetProjectClips")
            {
                if (request.Offset < 0 || request.Limit is < 1 or > 100) throw new InvalidDataException("Invalid clip page.");
                var project = coordinator.Current!;
                var timeline = ProjectTimeline.Build(project);
                return Reply(new(true, null, Snapshot(), project.Clips.Skip(request.Offset).Take(request.Limit).ToArray())
                    { TimelineClips = timeline.Clips.Skip(request.Offset).Take(request.Limit).ToArray() });
            }
            if (message.MessageType == "GetProjectWaveform")
            {
                if (request.ServerInstanceId != _instanceId || request.ExpectedRevision != coordinator.Current!.Revision)
                    throw new InvalidOperationException("Waveform request belongs to an obsolete snapshot.");
                if (waveforms is null) throw new PlatformNotSupportedException("Waveform decoder is not available on this platform.");
                var waveform = await waveforms.QueryAsync(request.ProjectId!.Value, request.ExpectedRevision, request.ClipId);
                return Reply(new(true, null, Snapshot()) { Waveform = waveform });
            }
            if (request.OperationId == Guid.Empty) throw new InvalidDataException("An operation ID is required.");
            if (_operations.Count >= 20000) throw new InvalidOperationException("Save and restart OpenCam before issuing more project commands.");
            if (waveforms is not null && message.MessageType is
                "StartProjectRecording" or "StartNewRecordingContent" or "CloseProject")
                await waveforms.SuspendAsync();
            if (frames is not null && message.MessageType is
                "StartProjectRecording" or "StartNewRecordingContent" or "CloseProject" or "OpenProject" or "CreateProject")
                await frames.SuspendAsync();
            ProjectCommandResult result = message.MessageType switch
            {
                "CreateProject" => await coordinator.CreateAsync(request.Path ?? "", request.Name ?? ""),
                "OpenProject" => await coordinator.OpenAsync(request.Path ?? ""),
                "StartNewRecordingContent" => await coordinator.StartNewContentAsync(request.Configuration ?? throw new InvalidDataException("Missing capture configuration."), request.OperationId),
                "FinishRecordingContent" => await coordinator.FinishAndExportAsync(request.OperationId),
                "RetryRecordingContentExport" => await coordinator.RetryExportAsync(request.OperationId),
                "CancelRecordingContentExport" => await coordinator.RequestExportCancellationAsync(request.ExportId ?? throw new InvalidDataException("Missing export identity.")),
                "StartProjectRecording" => await coordinator.StartAsync(request.Configuration ?? throw new InvalidDataException("Missing capture configuration."), request.OperationId),
                "PauseProjectRecording" => await coordinator.PauseAsync(request.OperationId),
                "FinishProjectRecording" => await coordinator.FinishAsync(request.OperationId),
                "SaveProject" => await coordinator.SaveAsync(request.ExpectedRevision),
                "RenameProjectClip" => await coordinator.RenameClipAsync(request.ClipId, request.Name ?? "", request.ExpectedRevision),
                "ApplyProjectEdit" => await coordinator.ApplyEditAsync(request.Edit ?? throw new InvalidDataException("Missing clip edit."),
                    request.ExpectedRevision, request.OperationId),
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

    // The companion media pipe has the same authentication and peer PID checks, but never accepts mutations.
    public async Task<IpcResponse> DispatchMediaAsync(IpcMessage message)
    {
        await _gate.WaitAsync();
        try
        {
            if (message.MessageType != "GetProjectFrame" || message.PayloadJson.Length > 65536)
                throw new InvalidDataException("Invalid media request.");
            var request = JsonSerializer.Deserialize<ProjectRequest>(message.PayloadJson,
                new JsonSerializerOptions { MaxDepth = 16 }) ?? throw new InvalidDataException("Missing request.");
            if (request.ServerInstanceId != _instanceId || request.ProjectId is null ||
                request.ProjectId != coordinator.Current?.ProjectId || request.ExpectedRevision != coordinator.Current.Revision)
                throw new InvalidOperationException("Preview request belongs to an obsolete snapshot.");
            if (frames is null) throw new PlatformNotSupportedException("Preview decoder is unavailable.");
            return Reply(new(true, null, Snapshot()) { Frame = await frames.QueryAsync(request.ProjectId.Value,
                request.ExpectedRevision, request.TimelineTicks) });
        }
        catch (Exception ex) { return Reply(new(false, ex.Message, Snapshot())); }
        finally { _gate.Release(); }
    }

    private static IpcResponse Reply(ProjectReply reply) => new() {
        Success = reply.Success, ErrorMessage = reply.Error, DataJson = JsonSerializer.Serialize(reply) };
}
