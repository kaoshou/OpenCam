// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectSubtitlesDialog : Window
{
    public ProjectSubtitlesDialog() : this(new ProjectWorkspaceViewModel(new ProjectClient((_, _, _) =>
        Task.FromResult(new ScreenRecorder.Infrastructure.IPC.IpcResponse { Success = false, ErrorMessage = "No project client configured." })))) { }
    private readonly ProjectWorkspaceViewModel _model;
    private readonly List<SubtitleRow> _rows = [];
    private bool _busy;
    private bool _defaultChosenByUser, _refreshingDefault;
    private LanguageManager Strings => LanguageManager.Instance;
    private sealed class SubtitleRow(ProjectSubtitleTrack track, bool unknown = false)
    {
        public ProjectSubtitleTrack Track = track;
        public string Language = track.Language;
        public bool Unknown = unknown;
        public int Number;
        public override string ToString() => $"{Number}. {Track.OriginalName} · {Language}";
    }
    private sealed record LanguageChoice(string? Code, string Label)
    { public override string ToString() => Label; }
    private LanguageChoice[] Languages => new[] { "zh-TW", "zh-CN", "en", "ko", "ja", "vi", "th", "id", "ms", "fr", "de", "es", "pt", "ar", "ru" }
        .Select(code => new LanguageChoice(code, Label(code))).Append(new(null, Strings["SubtitleCustomLanguage"])).ToArray();
    private static string Label(string code)
    {
        if (code == "zh-TW") return "繁體中文（台灣） · zh-TW";
        try { return System.Globalization.CultureInfo.GetCultureInfo(code).NativeName + " · " + code; }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }

    public ProjectSubtitlesDialog(ProjectWorkspaceViewModel model)
    {
        _model = model; InitializeComponent();
        Title = Heading.Text = Strings["SubtitleAttach"];
        Description.Text = Strings["SubtitleWebOnly"];
        SelectFilesButton.Content = Strings["SubtitleSelect"];
        DefaultLabel.Text = Strings["SubtitleDefault"];
        DropHint.Text = Strings["SubtitleDropHint"];
        CancelSubtitleButton.Content = Strings["Cancel"];
        ApplySubtitleButton.Content = Strings["SubtitleApply"];
        ConfirmSubtitleSync.Content = Strings["SubtitleConfirmSync"];
        ConfirmSubtitleSync.IsVisible = model.State.SubtitlesNeedSyncConfirmation;
        _defaultChosenByUser = model.State.Subtitles.DefaultTrackId is not null;
        DefaultSubtitlePicker.SelectionChanged += (_, _) => { if (!_refreshingDefault) _defaultChosenByUser = true; };
        _rows.AddRange(model.State.Subtitles.Tracks.Select(t => new SubtitleRow(t)));
        DragDrop.SetAllowDrop(DropSurface, true);
        DropSurface.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropSurface.AddHandler(DragDrop.DropEvent, OnDrop);
        RenderRows(model.State.Subtitles.DefaultTrackId);
    }

    private void RenderRows(Guid? preferred = null)
    {
        preferred ??= (DefaultSubtitlePicker.SelectedItem as SubtitleRow)?.Track.Id;
        SubtitleRows.Children.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i]; row.Number = i + 1;
            var name = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 0) };
            name.Children.Add(new TextBlock { Text = $"{row.Number}. {row.Track.OriginalName}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            name.Children.Add(new TextBlock { Text = Path.GetExtension(row.Track.RelativePath).TrimStart('.').ToUpperInvariant(), FontSize = 11, Foreground = Brushes.SlateGray });
            if (row.Unknown) name.Children.Add(new TextBlock { Text = Strings["SubtitleUnknown"], FontSize = 11, Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap });
            var languages = Languages;
            var picker = new ComboBox { ItemsSource = languages, SelectedItem = languages.FirstOrDefault(l => l.Code == row.Language) ?? languages[^1], MinWidth = 165 };
            var custom = new TextBox { Text = row.Language, IsVisible = (picker.SelectedItem as LanguageChoice)?.Code is null, MaxLength = 64 };
            var languagePanel = new StackPanel { Spacing = 5, Children = { picker, custom }, Margin = new Thickness(0, 0, 10, 0) };
            custom.TextChanged += (_, _) => { row.Language = custom.Text ?? ""; RefreshDefault(); };
            picker.SelectionChanged += (_, _) => {
                var selected = picker.SelectedItem as LanguageChoice;
                custom.IsVisible = selected?.Code is null;
                row.Language = selected?.Code ?? custom.Text ?? "";
                row.Unknown = false; RefreshDefault();
            };
            var remove = new Button { Content = Strings["SubtitleRemove"], HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) => {
                if ((DefaultSubtitlePicker.SelectedItem as SubtitleRow)?.Track.Id == row.Track.Id) _defaultChosenByUser = false;
                _rows.Remove(row); RenderRows();
            };
            var grid = new Grid { ColumnDefinitions = new("*,185,Auto"), Children = { name, languagePanel, remove } };
            Grid.SetColumn(languagePanel, 1); Grid.SetColumn(remove, 2);
            SubtitleRows.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brush.Parse("#E2E8F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = grid });
        }
        RefreshDefault(preferred);
    }

    private void RefreshDefault(Guid? preferred = null)
    {
        preferred ??= (DefaultSubtitlePicker.SelectedItem as SubtitleRow)?.Track.Id;
        _refreshingDefault = true;
        try
        {
            DefaultSubtitlePicker.ItemsSource = _rows.ToArray();
            DefaultSubtitlePicker.SelectedItem = _rows.FirstOrDefault(r => r.Track.Id == preferred) ??
                _rows.FirstOrDefault(r => r.Language == "zh-TW") ?? _rows.FirstOrDefault();
        }
        finally { _refreshingDefault = false; }
        DefaultSubtitlePicker.IsEnabled = !_busy && _rows.Count > 0;
        Notice.Text = _rows.Count == 0 ? Strings["SubtitleDefaultInitial"] :
            !_defaultChosenByUser && _rows.All(r => r.Language != "zh-TW") ? Strings["SubtitleDefaultFallback"] : "";
    }

    private void SetBusy(bool value)
    {
        _busy = value; SelectFilesButton.IsEnabled = !value; ApplySubtitleButton.IsEnabled = !value;
        CancelSubtitleButton.IsEnabled = !value; SubtitleRows.IsEnabled = !value; DefaultSubtitlePicker.IsEnabled = !value && _rows.Count > 0;
    }
    private async void OnSelectFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Strings["SubtitleSelect"], AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("SRT / VTT") { Patterns = ["*.srt", "*.vtt"] }] });
        await ImportFilesAsync(files.Select(f => f.TryGetLocalPath()));
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    { e.DragEffects = !_busy && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void OnDrop(object? sender, DragEventArgs e)
    { e.Handled = true; await ImportFilesAsync((e.Data.GetFiles() ?? []).OfType<IStorageFile>().Select(f => f.TryGetLocalPath())); }

    private async Task ImportFilesAsync(IEnumerable<string?> files)
    {
        if (_busy) return;
        SetBusy(true); ErrorText.Text = "";
        try
        {
            foreach (var path in files)
            {
                if (path is null || Path.GetExtension(path).ToLowerInvariant() is not (".srt" or ".vtt"))
                { ErrorText.Text = Strings["SubtitleLocalOnly"]; continue; }
                var language = ProjectSubtitleLanguage.Infer(Path.GetFileName(path)) ?? "zh-TW";
                var duplicates = _rows.Where(r => r.Language == language).ToArray();
                SubtitleRow? replace = null;
                if (duplicates.Length > 0)
                {
                    var choice = await ChooseDuplicateAsync(duplicates);
                    if (choice is null) continue;
                    if (choice >= 0) replace = duplicates[choice.Value];
                }
                if (_rows.Count >= 32 && replace is null) { ErrorText.Text = Strings["SubtitleLocalOnly"]; break; }
                var track = await _model.ImportSubtitleAsync(path);
                if (track is null) { ErrorText.Text = _model.Error ?? Strings["SubtitleLocalOnly"]; continue; }
                var oldDefault = (DefaultSubtitlePicker.SelectedItem as SubtitleRow)?.Track.Id;
                if (replace is not null) _rows.Remove(replace);
                _rows.Add(new(track, ProjectSubtitleLanguage.Infer(Path.GetFileName(path)) is null));
                var preferred = !_defaultChosenByUser ? (_rows.FirstOrDefault(r => r.Language == "zh-TW") ?? _rows.FirstOrDefault())?.Track.Id :
                    replace is not null && replace.Track.Id == oldDefault ? track.Id : oldDefault;
                RenderRows(preferred);
            }
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async Task<int?> ChooseDuplicateAsync(SubtitleRow[] rows)
    {
        var dialog = new Window { Title = Strings["SubtitleAttach"], Width = 530, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
        var picker = new ComboBox { ItemsSource = rows, SelectedIndex = 0 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Button Add(string label, Action click)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 8), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => click(); actions.Children.Add(button); return button;
        }
        Add(Strings["Cancel"], () => dialog.Close((int?)null)).IsCancel = true;
        Add(Strings["SubtitleKeep"], () => dialog.Close((int?)-1));
        Add(Strings["SubtitleReplace"], () => dialog.Close((int?)picker.SelectedIndex));
        dialog.Content = new StackPanel { Margin = new Thickness(22), Spacing = 16, Children = {
            new TextBlock { Text = Strings["SubtitleDuplicate"], TextWrapping = TextWrapping.Wrap }, picker, actions } };
        return await dialog.ShowDialog<int?>(this);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true); ErrorText.Text = "";
        try
        {
            var settings = new ProjectSubtitleSettings(_rows.Select(r => r.Track with { Language = ProjectSubtitleLanguage.Normalize(r.Language) }).ToImmutableArray(),
                (DefaultSubtitlePicker.SelectedItem as SubtitleRow)?.Track.Id);
            ProjectValidation.ValidateSubtitles(settings);
            await _model.SetSubtitlesAsync(settings, ConfirmSubtitleSync.IsChecked == true);
            if (_model.StatusUnconfirmed || _model.Error is not null) { ErrorText.Text = _model.Error; return; }
            Close(true);
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
        finally { SetBusy(false); }
    }
}
