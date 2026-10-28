// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectWorkspaceView
{
    private void InitializeVideoImport()
    {
        DragDrop.SetAllowDrop(ClipPane, true);
        ClipPane.AddHandler(DragDrop.DragOverEvent, OnVideoDragOver);
        ClipPane.AddHandler(DragDrop.DropEvent, OnVideoDrop);
    }

    private async void OnImportVideo(object? sender, RoutedEventArgs e)
    {
        if (!Model.CanImportVideo) return;
        try
        {
            var project = Model.State.ProjectId;
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = Model.Strings["VideoImportAdd"], AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Model.Strings["VideoImportFiles"])
                {
                    Patterns = ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.m4v"],
                    AppleUniformTypeIdentifiers = ["public.movie"]
                }]
            });
            if (project != Model.State.ProjectId) return;
            await Model.ImportVideosAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) { Model.Error = ex.Message; }
    }

    private void OnVideoDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = Model.CanImportVideo && e.Data.GetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnVideoDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Model.CanImportVideo) return;
        try
        {
            var paths = e.Data.GetFiles()?.OfType<IStorageFile>().Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            await Model.ImportVideosAsync(paths);
        }
        catch (Exception ex) { Model.Error = ex.Message; }
    }
}
