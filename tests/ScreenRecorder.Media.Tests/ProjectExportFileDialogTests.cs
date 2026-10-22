// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Platform.Storage;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectExportFileDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencam-export-picker-" + Guid.NewGuid().ToString("N"));
    public ProjectExportFileDialogTests() => Directory.CreateDirectory(_root);

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw, true, "請選擇新檔名", "更換檔名")]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw, false, "請選擇新檔名", "取消")]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs, true, "Choose a new filename", "Choose another name")]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs, false, "Choose a new filename", "Cancel")]
    public async Task ConflictDialogUsesLocalizedChoices(ScreenRecorder.Core.Localization.AppLanguage language,
        bool chooseAnother, string title, string choice)
    {
        var strings = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var previous = strings.CurrentLanguage;
        strings.CurrentLanguage = language;
        var owner = new Avalonia.Controls.Window();
        try
        {
            owner.Show();
            var result = ProjectSaveDialogs.AskNewExportDestinationAsync(owner);
            var dialog = Assert.Single(owner.OwnedWindows);
            Assert.Equal(title, dialog.Title);
            var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).ToArray();
            Assert.Contains(controls.OfType<Avalonia.Controls.TextBlock>(), b => b.Text == strings["ProjectExportPathExists"]);
            controls.OfType<Avalonia.Controls.Button>().Single(b => Equals(b.Content, choice))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(chooseAnother, await result);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            owner.Close();
            strings.CurrentLanguage = previous;
        }
    }

    [Theory]
    [InlineData("mp4")]
    [InlineData("gif")]
    public async Task ExistingDestinationRequiresAnotherNameBeforeReturningPath(string extension)
    {
        var existing = Path.Combine(_root, "existing." + extension);
        var fresh = Path.Combine(_root, "new." + extension);
        await File.WriteAllTextAsync(existing, "original content");
        var picks = new Queue<string?>([existing, fresh]);
        var warnings = 0;
        var result = await ProjectExportFileDialog.ChooseNewFileAsync("clip", extension, options =>
        {
            Assert.False(options.ShowOverwritePrompt);
            Assert.Equal(extension, options.DefaultExtension);
            Assert.EndsWith("." + extension, options.SuggestedFileName);
            return Task.FromResult(picks.Dequeue());
        }, () => { warnings++; return Task.FromResult(true); });
        Assert.Equal(fresh, result);
        Assert.Equal(1, warnings);
        Assert.Empty(picks);
        Assert.Equal("original content", await File.ReadAllTextAsync(existing));
        Assert.False(File.Exists(fresh));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelFromConflictOrReopenedPickerReturnsNoDestination(bool reopen)
    {
        var existing = Path.Combine(_root, "existing.mp4");
        await File.WriteAllTextAsync(existing, "keep");
        var picks = 0;
        Assert.Null(await ProjectExportFileDialog.ChooseNewFileAsync("clip", "mp4",
            _ => Task.FromResult<string?>(++picks == 1 ? existing : null), () => Task.FromResult(reopen)));
        Assert.Equal(reopen ? 2 : 1, picks);
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task RepeatedConflictsAndDirectoryNamesStayInPickerFlow()
    {
        var directory = Path.Combine(_root, "folder.gif");
        Directory.CreateDirectory(directory);
        var fresh = Path.Combine(_root, "new.gif");
        var picks = new Queue<string?>([directory, directory, fresh]);
        var warnings = 0;
        Assert.Equal(fresh, await ProjectExportFileDialog.ChooseNewFileAsync("clip", "gif",
            _ => Task.FromResult(picks.Dequeue()), () => { warnings++; return Task.FromResult(true); }));
        Assert.Equal(2, warnings);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task CancelInitialPickerDoesNotShowConflict()
    {
        Assert.Null(await ProjectExportFileDialog.ChooseNewFileAsync("clip", "mp4",
            _ => Task.FromResult<string?>(null), () => throw new InvalidOperationException("No conflict")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
