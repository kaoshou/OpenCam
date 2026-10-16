// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    public ObservableCollection<ProjectBookmarkPosition> Bookmarks { get; } = [];
    [ObservableProperty] private ProjectBookmarkPosition? _selectedBookmark;
    [ObservableProperty] private string _bookmarkName = "";
    [ObservableProperty] private string? _bookmarkError;
    [ObservableProperty] private string _newBookmarkName = "";
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool CanAddEditorBookmark => CanEditTimeline && State.ProjectId is not null && !_loadingBookmarks &&
        PlayheadTicks >= 0 && PlayheadTicks < DurationTicks;
    public bool CanDeleteBookmark => CanEditTimeline && SelectedBookmark is not null && Bookmarks.Contains(SelectedBookmark);
    public bool CanRenameBookmark => CanDeleteBookmark && !string.IsNullOrWhiteSpace(BookmarkName) && BookmarkName.Trim().Length <= 200;
    public string BookmarkPositionText => Strings["BookmarkCurrentPosition"] + " " + FormatTime(PlayheadTicks);
    private void NotifyBookmarkUi()
    {
        foreach (var property in new[] { nameof(HasBookmarks), nameof(CanAddEditorBookmark), nameof(CanDeleteBookmark), nameof(CanRenameBookmark), nameof(BookmarkPositionText) })
            OnPropertyChanged(property);
    }
    partial void OnBookmarkNameChanged(string value) => NotifyBookmarkUi();
    private long _bookmarkRevision;
    private bool _loadingBookmarks, _bookmarksEnabled, _restoringBookmarkSelection;
    partial void OnSelectedBookmarkChanged(ProjectBookmarkPosition? value)
    {
        BookmarkName = value?.Name ?? "";
        NotifyBookmarkUi();
        if (!_restoringBookmarkSelection && value?.TimelineTicks is long time && CanEditTimeline) Seek(time);
    }
    public async Task AddRecordingBookmarkAsync(string prefix)
    {
        if (State.Mode != ProjectMode.Recording || StatusUnconfirmed) return;
        var snapshot = State;
        try
        {
            // Nonessential marker IO must not occupy the capture/stop command's busy state.
            var reply = await client.SendAsync("AddProjectBookmark", new() { Name = prefix, OperationId = Guid.NewGuid(),
                ProjectId = snapshot.ProjectId, ServerInstanceId = snapshot.ServerInstanceId, ExpectedRevision = snapshot.Revision });
            if (reply.Unconfirmed || !reply.Success) { BookmarkError = reply.Error ?? "Bookmark save was not confirmed."; return; }
            if (reply.State.ProjectId != snapshot.ProjectId || reply.State.ServerInstanceId != snapshot.ServerInstanceId)
            { BookmarkError = "Recorder identity changed; bookmark save was not confirmed."; return; }
            BookmarkError = null;
            if (State.ProjectId == snapshot.ProjectId && State.ServerInstanceId == snapshot.ServerInstanceId && State.Mode == snapshot.Mode)
                ApplyReply(reply);
        }
        catch (Exception ex) { BookmarkError = ex.Message; }
    }
    [RelayCommand]
    public async Task AddEditorBookmarkAsync()
    {
        if (!CanAddEditorBookmark) return;
        var ticks = PlayheadTicks;
        var name = string.IsNullOrWhiteSpace(NewBookmarkName) ? Strings["BookmarkPrefix"] + " " + (Bookmarks.Count + 1) : NewBookmarkName.Trim();
        var old = Bookmarks.Select(b => b.Bookmark.Id).ToHashSet();
        _bookmarksEnabled = true;
        await ExecuteAsync("AddProjectBookmarkAtPlayhead", new() { TimelineTicks = ticks, Name = name, BookmarkRevision = _bookmarkRevision });
        if (Error is not null) BookmarkError = Error;
        if (Error is null && BookmarkError is null && !StatusUnconfirmed)
        {
            NewBookmarkName = "";
            SelectedBookmark = Bookmarks.Where(b => !old.Contains(b.Bookmark.Id))
                .OrderBy(b => b.TimelineTicks is long t ? Math.Abs(t - ticks) : long.MaxValue).FirstOrDefault();
        }
    }
    [RelayCommand] public async Task RenameBookmarkAsync()
    {
        if (!CanRenameBookmark) return;
        await ExecuteAsync("RenameProjectBookmark", new() { BookmarkId = SelectedBookmark!.Bookmark.Id, BookmarkRevision = _bookmarkRevision, Name = BookmarkName.Trim() });
        if (Error is not null) BookmarkError = Error;
    }
    [RelayCommand] public async Task DeleteBookmarkAsync()
    {
        if (!CanDeleteBookmark) return;
        await ExecuteAsync("DeleteProjectBookmark", new() { BookmarkId = SelectedBookmark!.Bookmark.Id, BookmarkRevision = _bookmarkRevision });
        if (Error is not null) BookmarkError = Error;
    }

    [RelayCommand]
    public async Task RefreshBookmarksAsync()
    {
        _bookmarksEnabled = true;
        if (_loadingBookmarks || State.ProjectId is null) return;
        _loadingBookmarks = true;
        NotifyBookmarkUi();
        var snapshot = State;
        try
        {
            var loaded = new List<ProjectBookmarkPosition>();
            long? revision = null; int? total = null;
            do
            {
                var page = await client.SendAsync("GetProjectBookmarks", new() { ProjectId = snapshot.ProjectId,
                    ServerInstanceId = snapshot.ServerInstanceId, ExpectedRevision = snapshot.Revision, Offset = loaded.Count, Limit = 100 });
                if (!page.Success || page.Unconfirmed || page.Bookmarks is null || page.BookmarkCount is < 0 or > 20000 ||
                    page.State.ProjectId != snapshot.ProjectId || page.State.ServerInstanceId != snapshot.ServerInstanceId || page.State.Revision != snapshot.Revision ||
                    revision is not null && revision != page.BookmarkRevision || total is not null && total != page.BookmarkCount ||
                    page.Bookmarks.Length != Math.Min(100, page.BookmarkCount - loaded.Count))
                    throw new InvalidDataException(page.Error ?? "Bookmark list changed; refresh to retry.");
                revision = page.BookmarkRevision; total = page.BookmarkCount; loaded.AddRange(page.Bookmarks);
            } while (loaded.Count < total);
            if (State.ProjectId != snapshot.ProjectId || State.ServerInstanceId != snapshot.ServerInstanceId || State.Revision != snapshot.Revision) return;
            var selected = SelectedBookmark?.Bookmark.Id;
            Bookmarks.Clear(); foreach (var mark in loaded) Bookmarks.Add(mark);
            _bookmarkRevision = revision ?? 0;
            // Refreshing metadata must not move the playhead.
            _restoringBookmarkSelection = true;
            try { SelectedBookmark = Bookmarks.FirstOrDefault(b => b.Bookmark.Id == selected); }
            finally { _restoringBookmarkSelection = false; }
            BookmarkError = null; OnPropertyChanged(nameof(Bookmarks));
        }
        catch (Exception ex) { BookmarkError = ex.Message; }
        finally { _loadingBookmarks = false; NotifyBookmarkUi(); }
    }
}
