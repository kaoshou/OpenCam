// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.Linux;

public sealed record LinuxSourceSelectionSnapshot(Guid ServerInstanceId, Guid OperationId, string Phase,
    LinuxPortalSelectionView? Selection = null, string? ErrorCode = null);
public sealed record LinuxSourceSelectionRequest(Guid OperationId, Guid? ServerInstanceId = null);
