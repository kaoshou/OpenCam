using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.Media.Tests;

public class ProjectBookmarkPanelTests
{
    [Fact]
    public async Task FailedListRefreshAfterSaveKeepsTheErrorVisibleAndNameAvailable()
    {
        var client = new Client { FailBookmarkRead = true };
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.NewBookmarkName = "Keep this name";
        await vm.AddEditorBookmarkAsync();
        Assert.Equal("Injected bookmark read failure", vm.BookmarkError);
        Assert.Equal("Keep this name", vm.NewBookmarkName);
    }

    [Fact]
    public async Task AddingInRepeatedMaterialKeepsThePlayheadAtTheSelectedOccurrence()
    {
        var client = new Client { DuplicateAppearance = true };
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.Seek(5_000_000);
        await vm.AddEditorBookmarkAsync();
        Assert.Equal(2, vm.Bookmarks.Count);
        Assert.Equal(5_000_000, vm.PlayheadTicks);
        Assert.Equal(5_000_000, vm.SelectedBookmark!.TimelineTicks);
    }
    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public async Task PanelAddsAtPlayheadAndDisablesUnselectedActions(AppLanguage language)
    {
        var previousLanguage = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = language;
        var client = new Client();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm, Width = 1024, Height = 640 };
        try
        {
            window.Show(); window.UpdateLayout();
            var flyout = Assert.IsType<Flyout>(window.FindControl<Button>("BookmarksButton")!.Flyout);
            flyout.ShowAt(window.FindControl<Button>("BookmarksButton")!);
            window.UpdateLayout();
            var panel = Assert.IsAssignableFrom<Control>(flyout.Content);
            Assert.InRange(panel.Bounds.Width, 350, 420);
            Assert.InRange(panel.Bounds.Height, 150, 560);
            var add = window.FindControl<Button>("AddEditorBookmarkButton");
            Assert.NotNull(add);
            var rename = window.FindControl<Button>("RenameBookmarkButton")!;
            var delete = window.FindControl<Button>("DeleteBookmarkButton")!;
            Assert.False(rename.IsEnabled); Assert.False(delete.IsEnabled);
            vm.Seek(5_000_000);
            window.FindControl<TextBox>("NewBookmarkNameBox")!.Text = "Example";
            Assert.True(add.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(add.Command);
            await command.ExecuteAsync(null);
            Assert.Equal("AddProjectBookmarkAtPlayhead", client.Mutation);
            Assert.Equal(5_000_000, client.Request!.TimelineTicks);
            Assert.Equal("Example", client.Request.Name);
            Assert.Single(vm.Bookmarks);
            var list = window.FindControl<ListBox>("BookmarksList")!;
            list.SelectedItem = vm.Bookmarks[0];
            Assert.Equal(vm.Bookmarks[0], vm.SelectedBookmark);
            Assert.Equal(5_000_000, vm.PlayheadTicks);
            vm.SelectedBookmark = vm.Bookmarks[0];
            Assert.True(delete.IsEnabled);
            vm.BookmarkName = "Renamed";
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(rename.Command).ExecuteAsync(null);
            Assert.Equal("Renamed", Assert.Single(vm.Bookmarks).Name);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(delete.Command).ExecuteAsync(null);
            Assert.Empty(vm.Bookmarks);
            client.FailMutation = true;
            await command.ExecuteAsync(null);
            Assert.Equal("Injected bookmark write failure", vm.BookmarkError);
            Assert.Empty(vm.Bookmarks);
            vm.Seek(vm.DurationTicks);
            Assert.False(add.IsEnabled);
            flyout.Hide();
        }
        finally { await window.RequestCloseAsync(); LanguageManager.Instance.CurrentLanguage = previousLanguage; }
    }

    private sealed class Client : IProjectClient
    {
        private readonly Guid _clip = Guid.NewGuid();
        private ProjectBookmarkPosition? _mark;
        private long _revision;
        public string? Mutation;
        public ProjectRequest? Request;
        public bool FailMutation;
        public bool DuplicateAppearance;
        public bool FailBookmarkRead;
        private readonly ProjectSnapshot _state = new(Guid.NewGuid(), "Test", "/tmp", 1, 1, ProjectMode.Ready, 1, false, false) { ServerInstanceId = Guid.NewGuid() };
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "GetProjectBookmarks" && FailBookmarkRead)
                return Task.FromResult(new ProjectReply(false, "Injected bookmark read failure", _state));
            if (command == "AddProjectBookmarkAtPlayhead" && FailMutation)
                return Task.FromResult(new ProjectReply(false, "Injected bookmark write failure", _state));
            if (command == "GetProjectClips") return Task.FromResult(new ProjectReply(true, null, _state,
                [new() { Id = _clip, Name = "Clip" }]) { TimelineClips = [new(_clip, 0, 10_000_000)] });
            if (command == "AddProjectBookmarkAtPlayhead")
            { Mutation = command; Request = request; _mark = new(new(request.OperationId, "session", "sessions/session/part.mkv", request.TimelineTicks, request.Name!), _clip, request.TimelineTicks); _revision++; }
            if (command == "RenameProjectBookmark") { _mark = _mark! with { Bookmark = _mark.Bookmark with { Name = request.Name! } }; _revision++; }
            if (command == "DeleteProjectBookmark") { _mark = null; _revision++; }
            var marks = _mark is null ? Array.Empty<ProjectBookmarkPosition>() : DuplicateAppearance
                ? new[] { _mark with { TimelineTicks = 0 }, _mark } : [_mark];
            return Task.FromResult(new ProjectReply(true, null, _state) { Bookmarks = marks, BookmarkCount = marks.Length, BookmarkRevision = _revision });
        }
    }
}
