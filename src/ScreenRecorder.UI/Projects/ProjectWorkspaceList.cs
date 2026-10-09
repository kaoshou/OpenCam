// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenRecorder.UI.Projects;

public sealed record ProjectClipDropTarget(Guid ClipId, Guid? BeforeClipId);

public sealed partial class ProjectWorkspaceViewModel
{
    [ObservableProperty] private bool _isClipListCompact;
    public string ClipListToggleIcon => IsClipListCompact ? "thumbnail" : "clips";
    partial void OnIsClipListCompactChanged(bool value)
    {
        OnPropertyChanged(nameof(ClipListToggleIcon));
    }

    public bool TryResolveDrop(Guid draggedId, Guid? targetId, bool after, out ProjectClipDropTarget target)
    {
        target = new(draggedId, null);
        if (!CanEditTimeline) return false;
        var dragged = Clips.ToList().FindIndex(c => c.Id == draggedId);
        if (dragged < 0) return false;
        var start = GroupStart(dragged);
        var end = GroupEnd(dragged);
        var insertion = Clips.Count;
        if (targetId is Guid id)
        {
            var index = Clips.ToList().FindIndex(c => c.Id == id);
            if (index < 0) return false;
            insertion = after ? GroupEnd(index) + 1 : GroupStart(index);
        }
        if (insertion >= start && insertion <= end + 1) return false;
        target = new(Clips[start].Id, insertion < Clips.Count ? Clips[insertion].Id : null);
        return true;
    }
}
