// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectBookmarkTooltipTests
{
    [AvaloniaFact]
    public async Task HoverShowsFullNameAndMovingAwayClosesItWithoutSeeking()
    {
        var vm = new ProjectWorkspaceViewModel(new Client());
        await vm.RefreshAsync();
        await vm.RefreshBookmarksAsync();
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var window = new Window { Content = timeline, Width = 800, Height = 240 };
        window.Show(); window.UpdateLayout();
        window.MouseMove(new(1, 1)); // Apply the headless window's initial client-size event before locating flags.
        try
        {
            Assert.Equal(2, vm.Bookmarks.Count);
            Assert.Equal(40_000_000, vm.DurationTicks);
            var x = 16 + (timeline.Bounds.Width - 32) / 4;
            window.MouseMove(new(x + 3, 38));
            Assert.Equal("第一個重點：完整名稱 / First bookmark", ToolTip.GetTip(timeline));
            Assert.True(ToolTip.GetIsOpen(timeline));
            Assert.Equal(0, vm.PlayheadTicks);
            Assert.Null(vm.SelectedBookmark);

            window.MouseMove(new(x + 3, 90));
            Assert.False(ToolTip.GetIsOpen(timeline));
            Assert.Null(ToolTip.GetTip(timeline));

            window.MouseMove(new(x + 3, 38));
            Assert.True(ToolTip.GetIsOpen(timeline));
            // Leave the control entirely, not just the marker.
            window.MouseMove(new(-10, -10));
            Assert.False(ToolTip.GetIsOpen(timeline));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MovingBetweenMarkersUpdatesNameAndClickStillSeeks()
    {
        var vm = new ProjectWorkspaceViewModel(new Client());
        await vm.RefreshAsync();
        await vm.RefreshBookmarksAsync();
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var window = new Window { Content = timeline, Width = 800, Height = 240 };
        window.Show(); window.UpdateLayout();
        window.MouseMove(new(1, 1));
        try
        {
            var first = new Point(16 + (timeline.Bounds.Width - 32) / 4, 38);
            var second = new Point(16 + (timeline.Bounds.Width - 32) * .75, 38);
            window.MouseMove(first);
            Assert.Equal("第一個重點：完整名稱 / First bookmark", ToolTip.GetTip(timeline));
            window.MouseMove(second);
            Assert.Equal("Second bookmark", ToolTip.GetTip(timeline));
            window.MouseDown(second, MouseButton.Left);
            window.MouseUp(second, MouseButton.Left);
            Assert.Equal("Second bookmark", vm.SelectedBookmark!.Name);
            Assert.Equal(30_000_000, vm.PlayheadTicks);
            Assert.False(ToolTip.GetIsOpen(timeline));

            window.MouseMove(first);
            Assert.True(ToolTip.GetIsOpen(timeline));
            timeline.Fit();
            Assert.False(ToolTip.GetIsOpen(timeline));
            window.MouseMove(second);
            Assert.True(ToolTip.GetIsOpen(timeline));
            timeline.DataContext = null;
            Assert.False(ToolTip.GetIsOpen(timeline));
        }
        finally { window.Close(); }
    }

    private sealed class Client : IProjectClient
    {
        private readonly Guid _clip = Guid.NewGuid();
        private readonly ProjectSnapshot _state = new(Guid.NewGuid(), "Test", "/tmp", 1, 1,
            ProjectMode.Ready, 1, false, false) { ServerInstanceId = Guid.NewGuid() };
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "GetProjectClips") return Task.FromResult(new ProjectReply(true, null, _state,
                [new() { Id = _clip, Name = "Clip" }]) { TimelineClips = [new(_clip, 0, 40_000_000)] });
            ProjectBookmarkPosition[] marks = [
                new(new(Guid.NewGuid(), "session", "sessions/session/part.mkv", 10_000_000,
                    "第一個重點：完整名稱 / First bookmark"), _clip, 10_000_000),
                new(new(Guid.NewGuid(), "session", "sessions/session/part.mkv", 30_000_000,
                    "Second bookmark"), _clip, 30_000_000)];
            return Task.FromResult(new ProjectReply(true, null, _state)
                { Bookmarks = marks, BookmarkCount = 2, BookmarkRevision = 1 });
        }
    }
}
