// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    // Capability follows the retained source intervals, including compound clips.
    // A baked cursor in legacy media cannot safely receive an editable overlay.
    public bool HasEditablePointerSources
    {
        get
        {
            if (Current is not { } project) return false;
            // Even incomplete metadata must remain editable so the user can explicitly
            // choose Hidden and export retained video. Visible rendering verifies the track.
            var sourceIds = project.Sources.Where(s => s.Pointer is not null).Select(s => s.Id).ToHashSet();
            return project.Clips.SelectMany(ProjectClipProjection.Leaves).Any(c => sourceIds.Contains(c.SourceId));
        }
    }

    public Task<ProjectCommandResult> SetPointerSettingsAsync(ProjectPointerSettings settings, long revision,
        CancellationToken ct = default) => EditAsync(revision, history =>
    {
        ProjectPointers.Validate(settings);
        if (!HasEditablePointerSources)
            throw new InvalidOperationException("This project has no editable pointer source. Existing baked cursors cannot be changed.");
        history.SetPointerSettings(settings);
    }, ct);
}
