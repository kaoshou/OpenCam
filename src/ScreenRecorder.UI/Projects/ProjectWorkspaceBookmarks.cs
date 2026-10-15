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
    private long _bookmarkRevision;
    private bool _loadingBookmarks, _bookmarksEnabled, _restoringBookmarkSelection;
    partial void OnSelectedBookmarkChanged(ProjectBookmarkPosition? value)
    {
        BookmarkName = value?.Name ?? "";
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
    [RelayCommand] public Task RenameBookmarkAsync() => !CanEditTimeline || SelectedBookmark is null ? Task.CompletedTask :
        ExecuteAsync("RenameProjectBookmark", new() { BookmarkId = SelectedBookmark.Bookmark.Id, BookmarkRevision = _bookmarkRevision, Name = BookmarkName });
    [RelayCommand] public Task DeleteBookmarkAsync() => !CanEditTimeline || SelectedBookmark is null ? Task.CompletedTask :
        ExecuteAsync("DeleteProjectBookmark", new() { BookmarkId = SelectedBookmark.Bookmark.Id, BookmarkRevision = _bookmarkRevision });

    [RelayCommand]
    public async Task RefreshBookmarksAsync()
    {
        _bookmarksEnabled = true;
        if (_loadingBookmarks || State.ProjectId is null) return;
        _loadingBookmarks = true;
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
        finally { _loadingBookmarks = false; }
    }
}
