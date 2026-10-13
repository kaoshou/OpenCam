using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectWorkspaceView
{
    private bool _seekDragging, _syncSeek;
    private void InitializeFullscreenSeek()
    {
        FullscreenSeek.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(FullscreenSeek).Properties.IsLeftButtonPressed)
            {
                _seekDragging = true;
                if (DataContext is ProjectWorkspaceViewModel vm) vm.BeginPreviewScrub();
            }
        }, RoutingStrategies.Tunnel);
        FullscreenSeek.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (!_seekDragging) return;
            _seekDragging = false;
            if (DataContext is ProjectWorkspaceViewModel vm) { vm.Seek((long)FullscreenSeek.Value); _ = vm.EndPreviewScrubAsync(); }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        FullscreenSeek.PointerCaptureLost += (_, _) =>
            Dispatcher.UIThread.Post(() => {
                _seekDragging = false;
                if (DataContext is ProjectWorkspaceViewModel vm) _ = vm.EndPreviewScrubAsync();
                SyncFullscreenSeek();
            });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => SyncFullscreenSeek();
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }
    private void SyncFullscreenSeek()
    {
        if (_seekDragging || DataContext is not ProjectWorkspaceViewModel vm) return;
        _syncSeek = true;
        try { FullscreenSeek.Value = vm.PlayheadTicks; }
        finally { _syncSeek = false; }
    }
    private void OnFullscreenSeekChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncSeek) return;
        if (IsPreviewFullscreen && DataContext is ProjectWorkspaceViewModel vm)
            vm.Seek((long)e.NewValue);
        ShowPreviewControls();
    }
}
