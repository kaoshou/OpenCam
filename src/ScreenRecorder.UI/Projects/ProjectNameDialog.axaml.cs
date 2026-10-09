// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectNameDialog : Window
{
    public ProjectNameDialog() : this("", false) { }

    public ProjectNameDialog(string name, bool rename)
    {
        InitializeComponent();
        var strings = LanguageManager.Instance;
        Title = Heading.Text = strings[rename ? "ProjectRenameAction" : "HomeProjectNew"];
        Description.Text = strings[rename ? "ProjectRenameDescription" : "ProjectCreateDescription"];
        HeadingIcon.Icon = rename ? "rename" : "folder";
        NameLabel.Text = strings["HomeProjectName"];
        NameInput.Text = name;
        Avalonia.Automation.AutomationProperties.SetName(NameInput, strings["HomeProjectName"]);
        ConfirmButton.Content = strings[rename ? "ProjectRenameConfirm" : "Confirm"];
        CancelButton.Content = strings["Cancel"];
        NameInput.TextChanged += (_, _) => ValidationError.IsVisible = false;
        Opened += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        var name = NameInput.Text?.Trim() ?? "";
        try { ProjectValidation.ValidateName(name); }
        catch (InvalidDataException)
        {
            ValidationError.Text = LanguageManager.Instance["ProjectNameInvalid"];
            ValidationError.IsVisible = true;
            NameInput.Focus();
            return;
        }
        Close(name);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((string?)null);
}
