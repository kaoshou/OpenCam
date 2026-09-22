// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using System.Text.Json.Serialization;

namespace ScreenRecorder.Infrastructure.IPC;

public static class NamedPipeConstants
{
    public const string PipeBaseName = "ScreenRecorder_IPC_Pipe";
}

public class IpcMessage
{
    public string MessageType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public class StartRecordingCommand
{
    public RecordingConfiguration Configuration { get; set; } = new();
}

public class StopRecordingCommand
{
    public string Reason { get; set; } = "User requested stop";
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SessionId { get; set; }
    [JsonIgnore]
    public bool TimedOut { get; set; }
    [JsonIgnore]
    public bool StatusUnconfirmed { get; set; }
}
