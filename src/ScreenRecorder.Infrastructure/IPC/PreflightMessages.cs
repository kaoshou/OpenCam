// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;

namespace ScreenRecorder.Infrastructure.IPC;

public sealed record PreflightRequest(Guid OperationId, long ConfigurationRevision = 0,
    Guid? ServerInstanceId = null, RecordingConfiguration? Configuration = null, bool Confirm = false);
public sealed record PreflightReply(Guid ServerInstanceId, PreflightStatus Status);
