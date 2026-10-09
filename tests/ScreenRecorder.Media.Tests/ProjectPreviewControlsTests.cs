using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public class ProjectPreviewControlsTests
{
    [Theory]
    [InlineData(1, PreviewZoomMode.Actual,1920,1080)]
    [InlineData(2, PreviewZoomMode.Actual,960,540)]
    [InlineData(2, PreviewZoomMode.Double,1920,1080)]
    public void ActualSizeUsesRenderScaling(double scale, PreviewZoomMode zoom, double w, double h)
    {
        var size = ProjectWorkspaceView.PreviewPixelSize(1920,1080,scale,zoom);
        Assert.Equal(w,size.Width); Assert.Equal(h,size.Height);
    }
    [AvaloniaTheory]
    [InlineData(820)] [InlineData(1280)]
    public async Task PreviewControlsAndFullscreenPreserveEditor(double width)
    {
        var vm = new ProjectWorkspaceViewModel(new Client());
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm, Width = width, Height = 850 };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.NotNull(window.FindControl<ComboBox>("PreviewQualityPicker"));
            Assert.NotNull(window.FindControl<ComboBox>("PreviewZoomPicker"));
            Assert.NotNull(window.FindControl<GridSplitter>("PreviewSplitter"));
            var fullscreen = window.FindControl<Button>("PreviewFullscreenButton");
            Assert.NotNull(fullscreen);
            var image = window.FindControl<Image>("PreviewImage");
            fullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.False(window.FindControl<Border>("TimelinePane")!.IsVisible);
            Assert.Same(image, window.FindControl<Image>("PreviewImage"));
            fullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(window.FindControl<Border>("TimelinePane")!.IsVisible);
            Assert.False(vm.State.IsDirty);
        }
        finally { await window.RequestCloseAsync(); }
    }
    private sealed class Client : IProjectClient
    {
        private readonly ProjectSnapshot snapshot = new(Guid.NewGuid(),"Preview","/test",0,0,ProjectMode.Ready,0,false,false)
            { Canvas = new(1920,1080,new(30,1)) };
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true,null,command == "CloseProject" ? ProjectSnapshot.Closed : snapshot,[]));
    }
}
