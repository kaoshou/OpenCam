// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

internal static class ProjectExportFileDialog
{
    public static Task ExportAsync(Window owner, ProjectWorkspaceViewModel model, ProjectExportFormat format, Guid? clipId = null) =>
        model.RequestExportAsync(format, clipId, target => ProjectGifExportDialog.ShowAsync(owner, target),
            (target, selectedFormat) => ChooseAsync(owner, target, selectedFormat),
            target => ProjectMp4ExportDialog.ShowAsync(owner, target));

    public static Task ExportWebAsync(Window owner, ProjectWorkspaceViewModel model) =>
        model.RequestWebExportAsync(target => ProjectMp4ExportDialog.ShowAsync(owner, target, webPlayer: true),
            () => ChooseWebParentAsync(owner));

    public static Task<string?> ChooseAsync(Window owner, ProjectExportTarget target, ProjectExportFormat format)
    {
        var extension = format == ProjectExportFormat.Gif ? "gif" : "mp4";
        var clip = target.ClipId is Guid id ? $"_{id:N}" : "";
        return ChooseForWindowAsync(owner, target.Name + clip, extension);
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
    public static Task<string?> ChooseAsync(Window owner, string name) => ChooseForWindowAsync(owner, name, "mp4");

    private static Task<string?> ChooseForWindowAsync(Window owner, string name, string extension) =>
        ChooseNewFileAsync(name, extension,
            async options => (await owner.StorageProvider.SaveFilePickerAsync(options))?.TryGetLocalPath(),
            () => ProjectSaveDialogs.AskNewExportDestinationAsync(owner));

    internal static async Task<string?> ChooseNewFileAsync(string name, string extension,
        Func<FilePickerSaveOptions, Task<string?>> pick, Func<Task<bool>> chooseAnother)
    {
        if (extension is not ("mp4" or "gif")) throw new ArgumentOutOfRangeException(nameof(extension));
        var options = new FilePickerSaveOptions
        {
            Title = Localization.LanguageManager.Instance.GetFormatted("ProjectExportSaveTitle", extension.ToUpperInvariant()),
            SuggestedFileName = $"{ProjectNaming.FileStem(name)}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }],
            ShowOverwritePrompt = false
        };
        while (true)
        {
            var path = await pick(options);
            if (string.IsNullOrWhiteSpace(path)) return null;
            // An existing destination stays in the picker flow, never reaching
            // export. Recorder still enforces exclusive creation against races.
            if (!Path.Exists(path) && new FileInfo(path).LinkTarget is null) return path;
            if (!await chooseAnother()) return null;
            options.SuggestedFileName = Path.GetFileName(path);
        }
    }
}
