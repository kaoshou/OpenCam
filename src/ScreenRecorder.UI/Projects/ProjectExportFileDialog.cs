using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

internal static class ProjectExportFileDialog
{
    public static Task ExportAsync(Window owner, ProjectWorkspaceViewModel model, ProjectExportFormat format, Guid? clipId = null) =>
        model.RequestExportAsync(format, clipId, target => ProjectGifExportDialog.ShowAsync(owner, target),
            (target, selectedFormat) => ChooseAsync(owner, target, selectedFormat));

    public static async Task<string?> ChooseAsync(Window owner, ProjectExportTarget target, ProjectExportFormat format)
    {
        var extension = format == ProjectExportFormat.Gif ? "gif" : "mp4";
        var clip = target.ClipId is Guid id ? $"_{id:N}" : "";
        var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = format == ProjectExportFormat.Gif ? "GIF" : "MP4",
            SuggestedFileName = $"{ProjectNaming.FileStem(target.Name)}{clip}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }],
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }
    public static async Task<string?> ChooseWebParentAsync(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = Localization.LanguageManager.Instance["ProjectWebOutputLocation"],
            AllowMultiple = false
        });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }
    public static async Task<string?> ChooseAsync(Window owner, string name)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "MP4",
            SuggestedFileName = $"{ProjectNaming.FileStem(name)}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
            DefaultExtension = "mp4",
            FileTypeChoices = [new FilePickerFileType("MP4") { Patterns = ["*.mp4"] }],
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }
}
