// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

internal static class ProjectClipExportMenu
{
    public static MenuItem Create(ProjectWorkspaceViewModel model, Guid clipId, Func<ProjectExportFormat, Guid?, Task> export)
    {
        var project = model.State.ProjectId; var revision = model.State.Revision;
        var parent = new MenuItem { Header = model.Strings["ProjectExportClip"], IsEnabled = model.CanExport };
        foreach (var format in new[] { ProjectExportFormat.Mp4, ProjectExportFormat.Gif })
        {
            var child = new MenuItem { Header = format == ProjectExportFormat.Gif ? "GIF" : "MP4" };
            child.Click += async (_, e) =>
            {
                e.Handled = true;
                if (model.CanExport && model.State.ProjectId == project && model.State.Revision == revision &&
                    model.Clips.Any(c => c.Id == clipId)) await export(format, clipId);
            };
            parent.Items.Add(child);
        }
        return parent;
    }
}
