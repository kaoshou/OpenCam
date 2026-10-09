// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    [ObservableProperty] private bool _isClipListCompact;
    public string ClipListToggleIcon => IsClipListCompact ? "thumbnail" : "clips";
    partial void OnIsClipListCompactChanged(bool value)
    {
        OnPropertyChanged(nameof(ClipListToggleIcon));
    }
}
