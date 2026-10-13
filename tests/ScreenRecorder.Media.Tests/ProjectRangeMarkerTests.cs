// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectRangeMarkerTests
{
    [Fact]
    public async Task NewStartClearsPreviousEndAndInvalidEndCannotSelectOrDelete()
    {
        var vm = new ProjectWorkspaceViewModel(new RangeClient()); await vm.RefreshAsync();
        vm.SetRangeStart(20_000_000); vm.SetRangeEnd(60_000_000);
        Assert.True(vm.CanDeleteRange);
        vm.SetRangeStart(30_000_000);
        Assert.Null(vm.RangeEndTicks); Assert.False(vm.CanDeleteRange);
        vm.SetRangeEnd(20_000_000);
        Assert.Null(vm.RangeEndTicks); Assert.NotNull(vm.Error);
        vm.SetRangeEnd(30_000_000);
        Assert.Null(vm.RangeEndTicks); Assert.False(vm.CanDeleteRange);
        vm.SetRangeEnd(70_000_000);
        Assert.True(vm.CanDeleteRange); Assert.Null(vm.Error);
        Assert.False(vm.State.IsDirty);
        Assert.Equal(100_000_000, vm.DurationTicks);
    }

    [AvaloniaFact]
    public async Task StartIsDrawnBeforeEndAndStaysFixedWhenPlayheadMoves()
    {
        var vm = new ProjectWorkspaceViewModel(new RangeClient()); await vm.RefreshAsync();
        var timeline = new ProjectTimelineControl { DataContext = vm };
        var window = new Window { Content = timeline, Width = 1000, Height = 240 };
        try
        {
            window.Show(); window.UpdateLayout();
            vm.SetRangeStart(25_000_000); vm.Seek(75_000_000);
            var drawing = new DrawingGroup(); using (var context = drawing.Open()) timeline.Render(context);
            var x = 16 + (timeline.Bounds.Width - 32) / 4;
            Assert.Contains(GeometryDrawings(drawing), d => d.Pen?.Brush is ISolidColorBrush b && b.Color == Color.Parse("#0F766E")
                && Math.Abs(d.Geometry!.Bounds.X - x) < 1 && d.Geometry.Bounds.Height > 100);
            Assert.DoesNotContain(GeometryDrawings(drawing), d => d.Pen?.Brush is ISolidColorBrush b && b.Color == Color.Parse("#C2410C"));
            vm.SetRangeEnd(80_000_000);
            drawing = new DrawingGroup(); using (var context = drawing.Open()) timeline.Render(context);
            Assert.Contains(GeometryDrawings(drawing), d => d.Pen?.Brush is ISolidColorBrush b && b.Color == Color.Parse("#C2410C")
                && d.Geometry!.Bounds.Height > 100);
            Assert.Contains(GeometryDrawings(drawing), d => d.Brush is ISolidColorBrush b && b.Color.A == 36
                && d.Geometry!.Bounds.Width > 500);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<GeometryDrawing> GeometryDrawings(DrawingGroup group) => group.Children
        .SelectMany(d => d is DrawingGroup g ? GeometryDrawings(g) : d is GeometryDrawing geometry ? [geometry] : Array.Empty<GeometryDrawing>());

    private sealed class RangeClient : IProjectClient
    {
        private readonly ProjectClip clip = new() { Id = Guid.NewGuid(), Name = "Range", InPts = 0, OutPts = 10000 };
        private readonly ProjectSnapshot state = new(Guid.NewGuid(), "Range", "/test", 0, 0, ProjectMode.Ready, 1, false, false);
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, state, [clip]) { TimelineClips = [new(clip.Id, 0, 100_000_000)] });
    }
}
