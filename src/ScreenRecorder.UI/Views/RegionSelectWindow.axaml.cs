using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Views;

public partial class RegionSelectWindow : Window
{
    public LanguageManager Strings => LanguageManager.Instance;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private int _selectedX;
    private int _selectedY;
    private int _selectedWidth;
    private int _selectedHeight;
    private WindowEdge? _manualResizeEdge;
    private PixelRect _manualResizeInitialBounds;
    private PixelPoint _manualResizePointerStart;
    private bool _hasOpened;

    private const int MinimumRegionWidth = 320;
    private const int MinimumRegionHeight = 240;

    public int SelectedX => _selectedX;
    public int SelectedY => _selectedY;
    public int SelectedWidth => _selectedWidth;
    public int SelectedHeight => _selectedHeight;

    public RegionSelectWindow() : this(100, 100, 1280, 720) { }

    public RegionSelectWindow(int x, int y, int width, int height)
    {
        DataContext = this;
        InitializeComponent();

        if (OperatingSystem.IsMacOS())
        {
            TransparencyLevelHint =
                RegionSelectorAppearance.TransparencyLevels(isMacOS: true);
            TransparencyBackgroundFallback =
                new SolidColorBrush(
                    RegionSelectorAppearance.FallbackColor(isMacOS: true));
        }

        _selectedX = x;
        _selectedY = y;
        _selectedWidth = Math.Max(320, width);
        _selectedHeight = Math.Max(240, height);

        Position = new PixelPoint(x, y);
        var initialSize = OperatingSystem.IsMacOS()
            ? new Size(MinimumRegionWidth, MinimumRegionHeight)
            : RegionSelectionGeometry.ToLogicalSize(
                _selectedWidth,
                _selectedHeight,
                renderScaling: 1.0);
        Width = initialSize.Width;
        Height = initialSize.Height;

        UpdateSizeDisplay();

        Opened += (s, e) =>
        {
            if (RegionSelectorAppearance.RequiresNativeWindowTransparency(
                    OperatingSystem.IsMacOS()))
            {
                var platformHandle = TryGetPlatformHandle();
                if (platformHandle is not null)
                {
                    MacOsWindowTransparency.TryApply(platformHandle.Handle);
                }
            }

            Focus();
            if (RenderScaling > 0 && Math.Abs(RenderScaling - 1.0) > 0.001)
            {
                var logicalSize = RegionSelectionGeometry.ToLogicalSize(
                    _selectedWidth,
                    _selectedHeight,
                    RenderScaling);
                Width = logicalSize.Width;
                Height = logicalSize.Height;
            }
            _hasOpened = true;
            UpdateSizeDisplay();
        };

        PositionChanged += (s, e) =>
        {
            if (!_hasOpened)
            {
                return;
            }

            TryApplyEdgeSnapping();
            CaptureFinalBounds();
        };

        PointerMoved += OnManualResizePointerMoved;
        PointerReleased += OnManualResizePointerReleased;
        PointerCaptureLost += OnManualResizePointerCaptureLost;
    }

    private bool _isSnapping = false;

    private void TryApplyEdgeSnapping()
    {
        if (_isSnapping || _manualResizeEdge.HasValue) return;
        try
        {
            var screen = Screens.ScreenFromVisual(this);
            if (screen == null) return;

            var workArea = screen.WorkingArea;
            const int snapThreshold = 18;

            int currentX = Position.X;
            int currentY = Position.Y;
            double scaling = RenderScaling > 0 ? RenderScaling : 1.0;
            int currentW = (int)Math.Round(Width * scaling);
            int currentH = (int)Math.Round(Height * scaling);

            int targetX = currentX;
            int targetY = currentY;

            // 左邊界吸附
            if (Math.Abs(currentX - workArea.X) <= snapThreshold)
            {
                targetX = workArea.X;
            }
            // 右邊界吸附
            else if (Math.Abs((currentX + currentW) - (workArea.X + workArea.Width)) <= snapThreshold)
            {
                targetX = workArea.X + workArea.Width - currentW;
            }

            // 上邊界吸附
            if (Math.Abs(currentY - workArea.Y) <= snapThreshold)
            {
                targetY = workArea.Y;
            }
            // 下邊界吸附
            else if (Math.Abs((currentY + currentH) - (workArea.Y + workArea.Height)) <= snapThreshold)
            {
                targetY = workArea.Y + workArea.Height - currentH;
            }

            if (targetX != currentX || targetY != currentY)
            {
                _isSnapping = true;
                Position = new PixelPoint(targetX, targetY);
                _isSnapping = false;
            }
        }
        catch { }
    }

    private void CaptureFinalBounds()
    {
        try
        {
            var handle = TryGetPlatformHandle()?.Handle;
            if (OperatingSystem.IsWindows() &&
                handle.HasValue &&
                handle.Value != IntPtr.Zero &&
                GetWindowRect(handle.Value, out var rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 50 && h > 50)
                {
                    _selectedX = rect.Left;
                    _selectedY = rect.Top;
                    _selectedWidth = w % 2 == 0 ? w : w - 1;
                    _selectedHeight = h % 2 == 0 ? h : h - 1;
                    return;
                }
            }
        }
        catch { }

        var region = RegionSelectionGeometry.FromWindow(
            Position,
            ClientSize,
            RenderScaling);
        if (region.Width > 50 && region.Height > 50)
        {
            _selectedX = region.X;
            _selectedY = region.Y;
            _selectedWidth = region.Width;
            _selectedHeight = region.Height;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CaptureFinalBounds();
            Close(true);
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            if (!_hasOpened)
            {
                return;
            }

            CaptureFinalBounds();
            UpdateSizeDisplay();
        }
    }

    private void UpdateSizeDisplay()
    {
        var tb = this.FindControl<TextBlock>("SizeTextBlock");
        if (tb != null)
        {
            double scaling = RenderScaling > 0 ? RenderScaling : 1.0;
            int displayW = _selectedWidth > 0 ? _selectedWidth : (int)Math.Round(Width * scaling);
            int displayH = _selectedHeight > 0 ? _selectedHeight : (int)Math.Round(Height * scaling);
            tb.Text = $"{displayW} x {displayH}";
        }
    }

    private void OnDragMovePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnClientDoubleTapped(object? sender, RoutedEventArgs e)
    {
        CaptureFinalBounds();
        Close(true);
    }

    private void HandleResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var scaling = RenderScaling > 0 ? RenderScaling : 1.0;
            _manualResizeEdge = edge;
            _manualResizeInitialBounds = new PixelRect(
                Position.X,
                Position.Y,
                (int)Math.Round(ClientSize.Width * scaling),
                (int)Math.Round(ClientSize.Height * scaling));
            _manualResizePointerStart = this.PointToScreen(e.GetPosition(this));
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        BeginResizeDrag(edge, e);
    }

    private void OnManualResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_manualResizeEdge.HasValue)
        {
            return;
        }

        var bounds = RegionSelectionGeometry.ResizeWindow(
            _manualResizeInitialBounds,
            _manualResizePointerStart,
            this.PointToScreen(e.GetPosition(this)),
            _manualResizeEdge.Value,
            MinimumRegionWidth,
            MinimumRegionHeight);
        var scaling = RenderScaling > 0 ? RenderScaling : 1.0;

        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        e.Handled = true;
    }

    private void OnManualResizePointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!_manualResizeEdge.HasValue)
        {
            return;
        }

        _manualResizeEdge = null;
        e.Pointer.Capture(null);
        CaptureFinalBounds();
        UpdateSizeDisplay();
        e.Handled = true;
    }

    private void OnManualResizePointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _manualResizeEdge = null;
    }

    private void OnResizeTopPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.North, e);
    private void OnResizeBottomPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.South, e);
    private void OnResizeLeftPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.West, e);
    private void OnResizeRightPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.East, e);

    private void OnResizeTopLeftPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.NorthWest, e);
    private void OnResizeTopRightPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.NorthEast, e);
    private void OnResizeBottomLeftPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.SouthWest, e);
    private void OnResizeBottomRightPressed(object? sender, PointerPressedEventArgs e) => HandleResize(WindowEdge.SouthEast, e);

    private void OnFitCurrentScreenClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var screen = Screens.ScreenFromVisual(this) ?? Screens.Primary;
            if (screen != null)
            {
                double scaling = RenderScaling > 0 ? RenderScaling : (screen.Scaling > 0 ? screen.Scaling : 1.0);
                Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
                Width = screen.Bounds.Width / scaling;
                Height = screen.Bounds.Height / scaling;
                CaptureFinalBounds();
                UpdateSizeDisplay();
            }
        }
        catch { }
    }

    private void SetPresetDimensions(int targetPixelWidth, int targetPixelHeight)
    {
        double scaling = RenderScaling > 0 ? RenderScaling : 1.0;
        Width = targetPixelWidth / scaling;
        Height = targetPixelHeight / scaling;
        CaptureFinalBounds();
        UpdateSizeDisplay();
    }

    private void OnPreset1080pClicked(object? sender, RoutedEventArgs e) => SetPresetDimensions(1920, 1080);

    private void OnPreset720pClicked(object? sender, RoutedEventArgs e) => SetPresetDimensions(1280, 720);

    private void OnPreset480pClicked(object? sender, RoutedEventArgs e) => SetPresetDimensions(854, 480);

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        CaptureFinalBounds();
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
