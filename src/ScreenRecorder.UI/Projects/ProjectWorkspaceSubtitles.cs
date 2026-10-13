// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Projects;
using Avalonia.Layout;
using Avalonia.Media;

namespace ScreenRecorder.UI.Projects;

public sealed record PreviewSubtitleChoice(Guid? Id, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class ProjectWorkspaceViewModel
{
    private Guid? _previewSubtitleProject, _selectedSubtitleId, _loadedSubtitleId;
    private string? _loadedSubtitleHash;
    private ProjectSubtitleSettings _subtitleOptions = ProjectSubtitleSettings.Empty;
    private ProjectSubtitleCue[] _loadedSubtitleCues = [], _activeSubtitleCues = [];
    private bool _updatingSubtitleChoices;
    private long _subtitleIntent;
    private PreviewSubtitleChoice? _selectedSubtitleChoice;
    private string _subtitleText = "";
    public ObservableCollection<PreviewSubtitleChoice> PreviewSubtitleOptions { get; } = [];
    public string PreviewSubtitleText => _subtitleText;
    public bool HasPreviewSubtitle => _subtitleText.Length > 0;
    public bool HasSubtitleTracks => !State.Subtitles.Tracks.IsEmpty;
    public string SubtitleSyncWarning => State.SubtitlesNeedSyncConfirmation ? Strings["SubtitleSyncWarning"] : "";
    public PreviewSubtitleChoice? SelectedPreviewSubtitleChoice
    {
        get => _selectedSubtitleChoice;
        set { if (!_updatingSubtitleChoices && value != _selectedSubtitleChoice) _ = SelectPreviewSubtitleAsync(value?.Id); }
    }
    [ObservableProperty] private double _previewSubtitleScale = 100;
    [ObservableProperty] private double _previewSubtitleOpacity = .78;
    [ObservableProperty] private int _previewSubtitleTheme;
    [ObservableProperty] private int _previewSubtitlePosition = 2;
    private double _subtitleBaseSize = 24;
    public double PreviewSubtitleFontSize => _subtitleBaseSize * PreviewSubtitleScale / 100;
    public IBrush PreviewSubtitleForeground => PreviewSubtitleTheme == 1 ? Brushes.LightYellow : PreviewSubtitleTheme == 2 ? Brushes.Black : Brushes.White;
    public IBrush PreviewSubtitleBackground => PreviewSubtitleTheme == 3 ? Brushes.Transparent :
        new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(PreviewSubtitleOpacity, 0, 1) * 255),
            (byte)(PreviewSubtitleTheme == 2 ? 255 : 0), (byte)(PreviewSubtitleTheme == 2 ? 255 : 0), (byte)(PreviewSubtitleTheme == 2 ? 255 : 0)));
    public VerticalAlignment PreviewSubtitleAlignment => PreviewSubtitlePosition == 0 ? VerticalAlignment.Top : PreviewSubtitlePosition == 1 ? VerticalAlignment.Center : VerticalAlignment.Bottom;
    partial void OnPreviewSubtitleScaleChanged(double value) => OnPropertyChanged(nameof(PreviewSubtitleFontSize));
    partial void OnPreviewSubtitleOpacityChanged(double value) => OnPropertyChanged(nameof(PreviewSubtitleBackground));
    partial void OnPreviewSubtitleThemeChanged(int value)
    { OnPropertyChanged(nameof(PreviewSubtitleForeground)); OnPropertyChanged(nameof(PreviewSubtitleBackground)); }
    partial void OnPreviewSubtitlePositionChanged(int value) => OnPropertyChanged(nameof(PreviewSubtitleAlignment));
    public void SetSubtitleViewportWidth(double width)
    {
        var size = Math.Clamp(width * .025, 16, 38);
        if (Math.Abs(size - _subtitleBaseSize) < .5) return;
        _subtitleBaseSize = size; OnPropertyChanged(nameof(PreviewSubtitleFontSize));
    }
    public void ResetSubtitleAppearance()
    { PreviewSubtitleScale = 100; PreviewSubtitleOpacity = .78; PreviewSubtitleTheme = 0; PreviewSubtitlePosition = 2; }

    private void NotifySubtitles()
    {
        var settings = State.Subtitles;
        var newProject = _previewSubtitleProject != State.ProjectId;
        if (newProject || !_subtitleOptions.Tracks.SequenceEqual(settings.Tracks) || _subtitleOptions.DefaultTrackId != settings.DefaultTrackId)
        {
            var id = _selectedSubtitleId;
            if (newProject || (_subtitleOptions.Tracks.IsEmpty && !settings.Tracks.IsEmpty) ||
                (id is not null && settings.Tracks.All(t => t.Id != id)))
                id = settings.DefaultTrackId ?? settings.Tracks.FirstOrDefault()?.Id;
            if (newProject) { _loadedSubtitleCues = []; _loadedSubtitleId = null; _loadedSubtitleHash = null; }
            _previewSubtitleProject = State.ProjectId;
            _subtitleOptions = settings;
            _updatingSubtitleChoices = true;
            try
            {
                PreviewSubtitleOptions.Clear();
                PreviewSubtitleOptions.Add(new(null, Strings["SubtitleOff"]));
                for (var i = 0; i < settings.Tracks.Length; i++)
                {
                    var track = settings.Tracks[i];
                    PreviewSubtitleOptions.Add(new(track.Id, $"{i + 1}. {track.OriginalName} · {track.Language}"));
                }
            }
            finally { _updatingSubtitleChoices = false; }
            _ = SelectPreviewSubtitleAsync(id);
        }
        OnPropertyChanged(nameof(HasSubtitleTracks)); OnPropertyChanged(nameof(SubtitleSyncWarning));
    }

    public async Task SelectPreviewSubtitleAsync(Guid? trackId)
    {
        var intent = ++_subtitleIntent;
        _selectedSubtitleId = trackId;
        _selectedSubtitleChoice = PreviewSubtitleOptions.FirstOrDefault(c => c.Id == trackId);
        OnPropertyChanged(nameof(SelectedPreviewSubtitleChoice));
        _activeSubtitleCues = [];
        var state = State;
        var track = state.Subtitles.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null) { UpdateSubtitleText(); return; }
        if (_loadedSubtitleId == track.Id && _loadedSubtitleHash == track.Sha256)
        { _activeSubtitleCues = _loadedSubtitleCues; UpdateSubtitleText(); return; }
        UpdateSubtitleText();
        try
        {
            var reply = await client.SendAsync("GetProjectFrame", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, SubtitleTrackId = track.Id });
            if (intent != _subtitleIntent || State.ProjectId != state.ProjectId || State.Revision != state.Revision) return;
            if (!reply.Success || reply.Unconfirmed || reply.SubtitleTrackId != track.Id || reply.SubtitleData is null ||
                reply.State.ProjectId != state.ProjectId || reply.State.Revision != state.Revision || reply.State.ServerInstanceId != state.ServerInstanceId)
                throw new InvalidDataException(reply.Error ?? "Subtitle preview status is unconfirmed.");
            var cues = JsonSerializer.Deserialize<ProjectSubtitleCue[]>(reply.SubtitleData) ?? [];
            if (cues.Length is 0 or > 100000 || cues.Any(c => c.StartTicks < 0 || c.EndTicks <= c.StartTicks || c.Text is null || c.Text.Length > 10000))
                throw new InvalidDataException("Invalid subtitle preview data.");
            _loadedSubtitleId = track.Id; _loadedSubtitleHash = track.Sha256;
            _loadedSubtitleCues = cues; _activeSubtitleCues = cues; UpdateSubtitleText();
        }
        catch (Exception ex)
        {
            if (intent == _subtitleIntent && State.ProjectId == state.ProjectId && State.Revision == state.Revision)
            { Error = ex.Message; UpdateSubtitleText(); }
        }
    }

    private void UpdateSubtitleText()
    {
        var text = ProjectSubtitleTimeline.ActiveText(_activeSubtitleCues, PlayheadTicks);
        if (_subtitleText == text) return;
        _subtitleText = text;
        OnPropertyChanged(nameof(PreviewSubtitleText)); OnPropertyChanged(nameof(HasPreviewSubtitle));
    }

    public async Task<ProjectSubtitleTrack?> ImportSubtitleAsync(string path)
    {
        await _gate.WaitAsync();
        try
        {
            if (!CanEditTimeline) return null;
            IsBusy = true;
            var reply = await SendAsync("ImportProjectSubtitle", new() { Path = path,
                SubtitleLanguage = ProjectSubtitleLanguage.Infer(Path.GetFileName(path)) ?? "zh-TW" });
            ApplyReply(reply);
            if (!reply.Unconfirmed) { _pendingOperation = Guid.Empty; _pendingRequest = null; }
            return reply.Success && !reply.Unconfirmed ? reply.ImportedSubtitle : null;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
        finally { IsBusy = false; _gate.Release(); }
    }
    public Func<Task<SubtitleExportDecision>>? RequestSubtitleExportDecision { get; set; }
    public Task SetSubtitlesAsync(ProjectSubtitleSettings settings, bool confirmSync = false) => ExecuteAsync("EditProjectSubtitles", new() { SubtitleSettings = settings, ConfirmSubtitleSync = confirmSync });
}
