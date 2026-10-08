// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum ProjectCompletionPolicy { QuickMp4 = 0, KeepProjectSources = 1 }
public enum ProjectMode { Closed, Ready, Recording, SavingSegment, Paused, SaveFailed, Interrupted }
public sealed record ProjectCommandResult(bool Success, string? ErrorCode, Guid? ProjectId,
    long Revision, ProjectMode Mode, Guid? OperationId = null);
