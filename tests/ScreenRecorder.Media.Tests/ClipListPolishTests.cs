using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public class ClipListPolishTests
{
    [AvaloniaFact]
    public async Task ContextMenuTargetsClickedClipAndGroupRowsConnect()
    {
        var client = new Client();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm };
        window.Show();
        typeof(Window).GetMethod("HandleResized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [new Avalonia.Size(1440, 900), WindowResizeReason.User]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            var list = window.FindControl<ListBox>("ClipList")!;
            var rows = list.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(0, rows[0].Margin.Bottom);
            Assert.Equal(0, rows[1].Margin.Top);
            Assert.Equal(0, rows[0].BorderThickness.Bottom);
            Assert.Equal(0, rows[1].BorderThickness.Top);
            Assert.Equal(2, rows[2].Margin.Top);
            Assert.DoesNotContain(rows[0].GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("[") == true);
            rows[2].RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
            Assert.Equal(client.Clips[2].Id, vm.SelectedClip?.Id);
            var menu = list.ContextMenu!;
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(6, items.Length);
            Assert.False(items[1].IsEnabled); // Merge across group boundary is unsafe.
            Assert.False(items[2].IsEnabled); // No next clip.
            Assert.True(items[3].IsEnabled); // Group with the previous group.
            Assert.False(items[4].IsEnabled);
            items[5].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Contains(client.Requests, r => r.Edit is ProjectClipEdit.Remove remove && remove.ClipId == client.Clips[2].Id);
            var count = client.Requests.Count;
            vm.SelectedClip = vm.Clips[0];
            items[5].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(count, client.Requests.Count); // A stale popup cannot delete another selection.
            menu.Close();
            rows[0].RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
            Assert.Equal(7, list.ContextMenu!.Items.OfType<MenuItem>().Count());
            list.ContextMenu.Close();
        }
        finally { await window.RequestCloseAsync(); }
    }

    private sealed class Client : IProjectClient
    {
        public ProjectClip[] Clips { get; }
        public List<ProjectRequest> Requests { get; } = [];
        private readonly ProjectSnapshot _state = new(Guid.NewGuid(), "Test", "/test", 0, 0, ProjectMode.Ready, 3, false, false);
        public Client()
        {
            var group = Guid.NewGuid();
            Clips = [
                new() { Id = Guid.NewGuid(), Name = "Merged", GroupId = group, Parts = [new() { Id = Guid.NewGuid(), Name = "A" }] },
                new() { Id = Guid.NewGuid(), Name = "B", GroupId = group },
                new() { Id = Guid.NewGuid(), Name = "C" }];
        }
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProjectReply(true, null, _state, Clips) {
                TimelineClips = Clips.Select((clip, i) => new ProjectTimelineClip(clip.Id, i * 10000000L, (i + 1) * 10000000L)).ToArray() });
        }
    }
}
