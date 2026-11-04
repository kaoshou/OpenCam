// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Services;

namespace ScreenRecorder.UI.Views;

/// <summary>Explicit user actions only: no startup network calls, uploads or installer.</summary>
public sealed class SupportPanel : Border
{
    private readonly StackPanel _body = new() { Spacing = 10 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private LanguageManager Strings => LanguageManager.Instance;
    private CancellationTokenSource? _exportCancellation;

    public SupportPanel()
    {
        Background = Brushes.White; BorderBrush = Brush.Parse("#E2E8F0"); BorderThickness = new(1);
        CornerRadius = new(10); Padding = new(16, 13); Child = _body;
        Build();
        AttachedToVisualTree += (_, _) => { Strings.PropertyChanged += OnLanguageChanged; Build(); };
        DetachedFromVisualTree += (_, _) => { Strings.PropertyChanged -= OnLanguageChanged; _exportCancellation?.Cancel(); };
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Build();
    private void Build()
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = Strings["SupportTitle"], FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#0F172A") });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        AddButton(buttons, "SupportCheckUpdates", OpenUpdatesAsync, "SupportUpdateHint");
        AddButton(buttons, "SupportOpenLogs", OpenLogsAsync);
        AddButton(buttons, "SupportCopyEnvironment", CopyEnvironmentAsync);
        AddButton(buttons, "SupportExport", ExportAsync);
        _body.Children.Add(buttons);
        _body.Children.Add(new TextBlock { Text = Strings["SupportPrivacy"], TextWrapping = TextWrapping.Wrap,
            FontSize = 11, Foreground = Brush.Parse("#64748B"), LineHeight = 18 });
        _body.Children.Add(_status);
    }

    private void AddButton(Panel panel, string key, Func<Task> action, string? hint = null)
    {
        var button = new Button { Content = Strings[key], Margin = new(0, 0, 8, 6), Padding = new(10, 7),
            CornerRadius = new(7), FontSize = 12, Background = Brush.Parse("#F1F5F9"), Foreground = Brush.Parse("#334155") };
        AutomationProperties.SetName(button, Strings[key]); ToolTip.SetTip(button, Strings[hint ?? key]);
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = ""; }
            catch (Exception error)
            {
                Serilog.Log.Warning(error, "Support action failed: {Action}", key);
                _status.Text = Strings["SupportFailed"];
            }
        };
        panel.Children.Add(button);
    }

    private Task OpenUpdatesAsync()
    {
        using var process = Process.Start(new ProcessStartInfo(SupportDiagnostics.ReleasesUrl) { UseShellExecute = true });
        _status.Text = Strings[process is null ? "SupportUnavailable" : "SupportUpdateOpened"];
        return Task.CompletedTask;
    }
    private Task OpenLogsAsync()
    {
        Directory.CreateDirectory(SupportDiagnostics.LogDirectory);
        var result = PlatformFolderOpener.TryOpen(null, SupportDiagnostics.LogDirectory);
        _status.Text = Strings[result.Success ? "SupportOpened" : "SupportFailed"];
        return Task.CompletedTask;
    }
    private async Task CopyEnvironmentAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { _status.Text = Strings["SupportUnavailable"]; return; }
        await clipboard.SetTextAsync(SupportDiagnostics.EnvironmentSummary());
        _status.Text = Strings["SupportCopied"];
    }
    private async Task ExportAsync()
    {
        if (_exportCancellation is not null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null || !top.StorageProvider.CanSave) { _status.Text = Strings["SupportUnavailable"]; return; }
        using var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation; IsEnabled = false;
        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new() {
                Title = Strings["SupportExport"], SuggestedFileName = $"OpenCam-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip", ShowOverwritePrompt = false,
                FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }] });
            cancellation.Token.ThrowIfCancellationRequested();
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (path is null) { _status.Text = Strings["SupportUnavailable"]; return; }
            _status.Text = Strings["SupportBusy"];
            var summary = SupportDiagnostics.EnvironmentSummary();
            await Task.Run(() => SupportDiagnostics.ExportAsync(SupportDiagnostics.LogDirectory, path, summary, DateTime.Today, cancellation.Token), cancellation.Token);
            _status.Text = Strings["SupportSaved"];
        }
        finally { _exportCancellation = null; IsEnabled = true; }
    }
}
