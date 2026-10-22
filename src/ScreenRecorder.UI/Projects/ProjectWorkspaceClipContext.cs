// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

internal sealed record ProjectClipContextTarget(Guid? ProjectId, Guid? ServerInstanceId, long Revision, Guid ClipId);

public sealed partial class ProjectWorkspaceViewModel
{
    internal bool CanUseClipContext(ProjectClipContextTarget target) => CanEditTimeline &&
        State.ProjectId == target.ProjectId && State.ServerInstanceId == target.ServerInstanceId &&
        State.Revision == target.Revision && Clips.Any(c => c.Id == target.ClipId);

    internal Task RenameClipFromContextAsync(ProjectClipContextTarget target, string name) =>
        !CanUseClipContext(target) ? Task.CompletedTask : ExecuteAsync("RenameProjectClip",
            new() { ClipId = target.ClipId, Name = name, ServerInstanceId = target.ServerInstanceId },
            () => CanUseClipContext(target));

    internal Task DeleteClipFromContextAsync(ProjectClipContextTarget target) =>
        !CanUseClipContext(target) ? Task.CompletedTask : ExecuteAsync("ApplyProjectEdit",
            new() { Edit = new ProjectClipEdit.Remove(target.ClipId), ServerInstanceId = target.ServerInstanceId },
            () => CanUseClipContext(target));
}
