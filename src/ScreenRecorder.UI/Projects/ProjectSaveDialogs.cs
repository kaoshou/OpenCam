// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Layout;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

internal static class ProjectSaveDialogs
{
    public static Task<UnsavedDecision> AskUnsavedAsync(Window owner) => AskAsync(owner, "ProjectUnsavedPrompt",
        ("ProjectSave", UnsavedDecision.Save), ("ProjectDontSave", UnsavedDecision.Discard), ("Cancel", UnsavedDecision.Cancel));
    public static Task<bool?> AskRecoveryAsync(Window owner) => AskAsync<bool?>(owner, "ProjectDraftRecoveryPrompt",
        ("ProjectRecoverDraft", true), ("ProjectDiscardDraft", false), ("Cancel", null));
    public static Task<ExistingExportDecision> AskExistingExportAsync(Window owner) => AskAsync(owner, "ProjectExistingExportPrompt",
        ("ProjectOpenExisting", ExistingExportDecision.OpenExisting), ("ProjectExportAgain", ExistingExportDecision.ExportAgain),
        ("Cancel", ExistingExportDecision.Cancel));

    private static Task<T> AskAsync<T>(Window owner, string prompt, params (string Text, T Value)[] choices)
    {
        var strings = LanguageManager.Instance;
        var dialog = new Window { Title = strings["ContentEditor"], Width = 490, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, value) in choices)
        {
            var button = new Button { Content = strings[text], IsCancel = text == "Cancel" };
            button.Click += (_, _) => dialog.Close(value);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new(24), Spacing = 20, Children = {
            new TextBlock { Text = strings[prompt], TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons } };
        return dialog.ShowDialog<T>(owner);
    }
}
