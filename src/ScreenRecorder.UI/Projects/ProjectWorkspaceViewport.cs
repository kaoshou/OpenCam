// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public enum PreviewZoomMode { Fit, Actual, Double }

public partial class ProjectWorkspaceView
{
    public bool IsPreviewFullscreen { get; private set; }
    private PreviewZoomMode _previewZoom;
    private WindowState _previousWindowState;
    private PixelPoint _previousPosition;
    private Size _previousSize;
    private GridLength[]? _previousRows;
    private bool _previousClips, _previousInspector;
    private IInputElement? _previousFocus;
    private long _lastPreviewActivity;
    private DispatcherTimer? _previewControlsTimer;
    private Point? _panStart;
    private Vector _panOffset;
    private bool _syncQualityPicker;

    private void InitializePreviewViewport()
    {
        PreviewScroll.SizeChanged += (_, _) => UpdatePreviewViewport();
        PropertyChanged += (_, change) => { if (change.Property.Name == "RenderScaling") UpdatePreviewViewport(); };
        PointerMoved += (_, _) => ShowPreviewControls();
        PreviewScroll.PointerPressed += (_, e) => {
            if (_previewZoom == PreviewZoomMode.Fit || !e.GetCurrentPoint(PreviewScroll).Properties.IsLeftButtonPressed) return;
            _panStart = e.GetPosition(PreviewScroll); _panOffset = PreviewScroll.Offset;
            e.Pointer.Capture(PreviewScroll); e.Handled = true;
        };
        PreviewScroll.PointerMoved += (_, e) => {
            if (_panStart is not { } start) return;
            var delta = e.GetPosition(PreviewScroll) - start;
            PreviewScroll.Offset = new(Math.Clamp(_panOffset.X - delta.X, 0, Math.Max(0, PreviewScroll.Extent.Width - PreviewScroll.Viewport.Width)),
                Math.Clamp(_panOffset.Y - delta.Y, 0, Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height)));
        };
        PreviewScroll.PointerReleased += (_, e) => { _panStart = null; e.Pointer.Capture(null); };
        PreviewScroll.PointerCaptureLost += (_, _) => _panStart = null;
        _previewControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewControlsTimer.Tick += (_, _) => {
            if (IsPreviewFullscreen && Environment.TickCount64 - _lastPreviewActivity >= 3000 && !PreviewControls.IsKeyboardFocusWithin)
            { PreviewControls.Opacity = 0; PreviewControls.IsHitTestVisible = false; }
        };
        Closed += (_, _) => _previewControlsTimer.Stop();
    }

    private async void OnPreviewQualityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncQualityPicker || DataContext is not ProjectWorkspaceViewModel model || sender is not ComboBox picker || picker.SelectedIndex < 0) return;
        await model.SetPreviewQualityAsync((ProjectPreviewQuality)picker.SelectedIndex);
        _syncQualityPicker = true;
        try { picker.SelectedIndex = (int)model.PreviewQuality; } finally { _syncQualityPicker = false; }
    }
    private void OnPreviewZoomChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } picker) SetPreviewZoom((PreviewZoomMode)picker.SelectedIndex);
    }
    public void SetPreviewZoom(PreviewZoomMode zoom)
    {
        if (!Enum.IsDefined(zoom)) return;
        _previewZoom = zoom;
        if (PreviewScroll is null) return;
        PreviewScroll.Offset = default; UpdatePreviewViewport();
    }
    public static Size PreviewPixelSize(int width, int height, double renderScaling, PreviewZoomMode zoom)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(renderScaling) || renderScaling <= 0) return default;
        var factor = zoom == PreviewZoomMode.Double ? 2d : 1d;
        return new(width * factor / renderScaling, height * factor / renderScaling);
    }
    public void UpdatePreviewViewport()
    {
        if (PreviewImage is null || PreviewScroll is null) return;
        var size = _previewZoom == PreviewZoomMode.Fit ? PreviewScroll.Bounds.Size :
            PreviewImage.Source is Avalonia.Media.Imaging.Bitmap bitmap ? PreviewPixelSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height, RenderScaling, _previewZoom) : PreviewScroll.Bounds.Size;
        PreviewImage.Width = Math.Max(0, size.Width);
        PreviewImage.Height = Math.Max(0, size.Height);
    }
    private void OnPreviewFullscreen(object? sender, RoutedEventArgs e) => SetPreviewFullscreen(!IsPreviewFullscreen);
    public void SetPreviewFullscreen(bool fullscreen)
    {
        if (fullscreen == IsPreviewFullscreen) return;
        if (fullscreen)
        {
            _previousWindowState = WindowState; _previousPosition = Position; _previousSize = new(Width, Height);
            _previousRows = EditorRoot.RowDefinitions.Select(r => r.Height).ToArray();
            _previousClips = ClipPane.IsVisible; _previousInspector = InspectorPane.IsVisible;
            _previousFocus = FocusManager?.GetFocusedElement();
            IsPreviewFullscreen = true;
            EditorHeader.IsVisible = EditorFooter.IsVisible = TimelinePane.IsVisible = PreviewSplitter.IsVisible = false;
            ClipPane.IsVisible = InspectorPane.IsVisible = false; ApplyPanelWidths();
            for (var i = 0; i < EditorRoot.RowDefinitions.Count; i++) EditorRoot.RowDefinitions[i].Height = i == 2 ? new(1, GridUnitType.Star) : new(0);
            WindowState = WindowState.FullScreen;
            ShowPreviewControls(); _previewControlsTimer!.Start();
        }
        else
        {
            IsPreviewFullscreen = false; _previewControlsTimer!.Stop();
            WindowState = _previousWindowState;
            if (_previousWindowState == WindowState.Normal) { Position = _previousPosition; Width = _previousSize.Width; Height = _previousSize.Height; }
            EditorHeader.IsVisible = EditorFooter.IsVisible = TimelinePane.IsVisible = PreviewSplitter.IsVisible = true;
            ClipPane.IsVisible = _previousClips; InspectorPane.IsVisible = _previousInspector; ApplyPanelWidths();
            for (var i = 0; i < _previousRows!.Length; i++) EditorRoot.RowDefinitions[i].Height = _previousRows[i];
            ShowPreviewControls(); _previousFocus?.Focus();
        }
        UpdatePreviewViewport();
    }
    private void ShowPreviewControls()
    {
        if (PreviewControls is null) return;
        _lastPreviewActivity = Environment.TickCount64;
        PreviewControls.Opacity = 1; PreviewControls.IsHitTestVisible = true;
    }
}
