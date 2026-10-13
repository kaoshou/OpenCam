using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

internal static class ProjectExportFileDialog
{
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
