// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectTimelineContextMenuTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RightClickedVideoOrAudioClipCanBeDeletedWithoutSeekingOrDeletingSelection(bool audio)
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        vm.SelectedClip = vm.Clips[0]; vm.Seek(123);
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var owner = new Window { Content = timeline, Width = 800, Height = 240 };
        owner.Show(); owner.UpdateLayout();
        try
        {
            var point = new Point(16 + (timeline.Bounds.Width - 32) * .75,
                audio ? timeline.AudioTrackBounds.Center.Y : timeline.VideoTrackBounds.Center.Y);
            owner.MouseDown(point, MouseButton.Right); owner.MouseUp(point, MouseButton.Right);
            Assert.Equal(123, vm.PlayheadTicks);
            Assert.Equal(client.Clips[0].Id, vm.SelectedClip!.Id);
            var menu = Assert.IsType<ContextMenu>(timeline.ContextMenu);
            var rename = Assert.Single(menu.Items.OfType<MenuItem>(), i => Equals(i.Header, vm.Strings["ClipRenameAction"]));
            Assert.True(rename.IsEnabled);
            var export = Assert.Single(menu.Items.OfType<MenuItem>(), i => Equals(i.Header, vm.Strings["ProjectExportClip"]));
            Assert.Equal(2, export.ItemCount);
            Assert.Null(export.Command);
            var delete = Assert.Single(menu.Items.OfType<MenuItem>(), i => Equals(i.Header, vm.Strings["ProjectDeleteClip"]));
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var request = Assert.Single(client.Mutations).Request;
            Assert.Equal(client.Clips[1].Id, Assert.IsType<ProjectClipEdit.Remove>(request.Edit).ClipId);
            Assert.Equal(7, request.ExpectedRevision);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("server")]
    [InlineData("recording")]
    [InlineData("unknown")]
    [InlineData("export")]
    [InlineData("busy")]
    [InlineData("deleted")]
    public async Task DeleteRejectsStateChangedAfterMenuOpened(string change)
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var owner = new Window { Content = timeline, Width = 800, Height = 240 };
        owner.Show(); owner.UpdateLayout();
        try
        {
            var point = new Point(16 + (timeline.Bounds.Width - 32) * .75, timeline.VideoTrackBounds.Center.Y);
            owner.MouseDown(point, MouseButton.Right); owner.MouseUp(point, MouseButton.Right);
            var menu = Assert.IsType<ContextMenu>(timeline.ContextMenu);
            var delete = Assert.Single(menu.Items.OfType<MenuItem>(), i => Equals(i.Header, vm.Strings["ProjectDeleteClip"]));
            Change(vm, change);
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(client.Mutations);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("none")]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("server")]
    [InlineData("recording")]
    [InlineData("unknown")]
    [InlineData("export")]
    [InlineData("busy")]
    [InlineData("deleted")]
    public async Task RenameCapturesClipAndRechecksTargetAfterDialog(string change)
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        vm.SelectedClip = vm.Clips[0];
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var owner = new Window { Content = timeline, Width = 800, Height = 240 };
        owner.Show(); owner.UpdateLayout();
        try
        {
            var point = new Point(16 + (timeline.Bounds.Width - 32) * .75, timeline.VideoTrackBounds.Center.Y);
            owner.MouseDown(point, MouseButton.Right); owner.MouseUp(point, MouseButton.Right);
            var menu = Assert.IsType<ContextMenu>(timeline.ContextMenu);
            var rename = Assert.Single(menu.Items.OfType<MenuItem>(), i => Equals(i.Header, vm.Strings["ClipRenameAction"]));
            rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = Assert.IsType<ProjectNameDialog>(Assert.Single(owner.OwnedWindows));
            Assert.Equal("Second", dialog.FindControl<TextBox>("NameInput")!.Text);
            vm.SelectedClip = vm.Clips[0];
            Change(vm, change);
            dialog.FindControl<TextBox>("NameInput")!.Text = "Renamed";
            dialog.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (change != "none") Assert.Empty(client.Mutations);
            else
            {
                var mutation = Assert.Single(client.Mutations);
                Assert.Equal("RenameProjectClip", mutation.Command);
                Assert.Equal(client.Clips[1].Id, mutation.Request.ClipId);
                Assert.Equal("Renamed", mutation.Request.Name);
                Assert.Equal(7, mutation.Request.ExpectedRevision);
            }
        }
        finally { foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(); owner.Close(); }
    }

    private static void Change(ProjectWorkspaceViewModel vm, string change)
    {
        if (change == "revision") vm.State = vm.State with { Revision = 8 };
        if (change == "project") vm.State = vm.State with { ProjectId = Guid.NewGuid() };
        if (change == "server") vm.State = vm.State with { ServerInstanceId = Guid.NewGuid() };
        if (change == "recording") vm.State = vm.State with { Mode = ProjectMode.Recording };
        if (change == "unknown") vm.StatusUnconfirmed = true;
        if (change == "busy") vm.IsBusy = true;
        if (change == "export") vm.State = vm.State with { Export = new(Guid.NewGuid(), 7,
            RecordingExportState.Running, 0) };
        if (change == "deleted") vm.Clips.RemoveAt(1);
    }

    private sealed class ClipClient : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Movie", "/test", 7, 7, ProjectMode.Ready, 2, false, false);
        public ProjectClip[] Clips = [new() { Id = Guid.NewGuid(), Name = "First" }, new() { Id = Guid.NewGuid(), Name = "Second" }];
        public List<(string Command, ProjectRequest Request)> Mutations = [];
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command is "RenameProjectClip" or "ApplyProjectEdit") Mutations.Add((command, request));
            return Task.FromResult(new ProjectReply(true, null, State, command == "GetProjectClips" ? Clips : null)
            {
                TimelineClips = command == "GetProjectClips" ?
                    [new(Clips[0].Id, 0, 1000), new(Clips[1].Id, 1000, 2000)] : null
            });
        }
    }
}
