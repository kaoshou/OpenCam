// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    internal async Task<PointerTrackCursor> OpenPointerCursorAsync(Guid projectId, long revision,
        Guid sourceId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = Current;
            if (_handle is null || current?.ProjectId != projectId || current.Revision != revision ||
                Mode is not (ProjectMode.Ready or ProjectMode.Paused))
                throw new InvalidOperationException("Pointer media snapshot is no longer available.");
            var source = current.Sources.SingleOrDefault(s => s.Id == sourceId)
                ?? throw new InvalidDataException("Pointer source is not part of the current project.");
            return await PointerTrackCursor.OpenAsync(_handle, source, ct);
        }
        finally { _gate.Release(); }
    }
}
