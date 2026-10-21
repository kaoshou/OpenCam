// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects.Editor;

/// <summary>Draws canonical saved timeline spans. No simulated thumbnails or waveform data.</summary>
public sealed class ProjectTimelineControl : Control
{
    private ProjectWorkspaceViewModel? _model;
    private IPointer? _pointer;
    private Guid? _movingClip, _beforeClip;
    private long _gestureRevision;
    private double _downX, _zoom = 1, _offset;
    private bool _seeking, _dragging;
    private double TrackWidth => Math.Max(1, Bounds.Width - 32);
    internal Rect VideoTrackBounds => new(16, 52, TrackWidth, Math.Clamp(Bounds.Height - 102, 36, 76));
    internal Rect AudioTrackBounds => new(16, VideoTrackBounds.Bottom + 6, TrackWidth,
        Math.Max(0, Math.Min(44, Bounds.Height - VideoTrackBounds.Bottom - 8)));
    private double VisibleTicks => Math.Max(1, (_model?.DurationTicks ?? 0) / _zoom);
    private double X(long ticks) => 16 + (ticks - _offset) / VisibleTicks * TrackWidth;
    private long Ticks(double x) => (long)Math.Clamp(_offset + (x - 16) / TrackWidth * VisibleTicks, 0, _model?.DurationTicks ?? 0);

    public ProjectTimelineControl()
    {
        Focusable = true;
        ContextRequested += OnExportContextRequested;
        ClipToBounds = true;
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { CancelGesture(); if (_model is not null) _model.PropertyChanged -= Changed; _model = null; };
    }
    public event Func<ProjectExportFormat, Guid?, Task>? ExportRequested;
    private void OnExportContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ContextMenu?.Close(); ContextMenu = null; e.Handled = true;
        if (_model is not { CanEditTimeline: true } vm) return;
        Guid? id;
        if (e.TryGetPosition(this, out var point))
        {
            if (!VideoTrackBounds.Contains(point) && !AudioTrackBounds.Contains(point)) return;
            var ticks = Ticks(point.X);
            id = vm.TimelineClips.FirstOrDefault(c => ticks >= c.StartTicks && ticks < c.EndTicks)?.ClipId;
        }
        else id = vm.SelectedClip?.Id;
        if (id is not Guid clipId || vm.Clips.FirstOrDefault(c => c.Id == clipId) is not { } clip) return;
        CancelGesture(); vm.SelectedClip = clip;
        ContextMenu = new ContextMenu { Items = { ProjectClipExportMenu.Create(vm, clipId,
            (format, selected) => ExportRequested?.Invoke(format, selected) ?? Task.CompletedTask) } };
        ContextMenu.Open(this);
    }
    private void Bind()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as ProjectWorkspaceViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        Fit();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_model?.CanEditTimeline != true || _model.State.Revision != _gestureRevision) CancelGesture();
        _offset = Math.Clamp(_offset, 0, Math.Max(0, (_model?.DurationTicks ?? 0) - VisibleTicks));
        InvalidateVisual();
    }
    public void Fit() { CancelGesture(); _zoom = 1; _offset = 0; InvalidateVisual(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) CancelGesture();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var ink = new SolidColorBrush(Color.Parse(dark ? "#CAD5E3" : "#526174"));
        var stroke = new Pen(new SolidColorBrush(Color.Parse(dark ? "#455060" : "#DFE5ED")), 1);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_model is not { } vm) return;
        for (var i = 0; i <= 8; i++)
        {
            var x = 16 + TrackWidth * i / 8;
            var ticks = (long)Math.Min(long.MaxValue, _offset + VisibleTicks * i / 8);
            context.DrawLine(stroke, new(x, 30), new(x, Bounds.Height));
            var time = TimeSpan.FromTicks(ticks);
            Text(context, $"{(long)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}", x + 3, 8, ink, 80, 12);
        }
        var byId = vm.Clips.ToDictionary(c => c.Id);
        foreach (var span in vm.TimelineClips)
        {
            var left = X(span.StartTicks); var right = X(span.EndTicks);
            if (right < 0 || left > Bounds.Width || !byId.TryGetValue(span.ClipId, out var clip)) continue;
            var selected = vm.SelectedClip?.Id == clip.Id;
            var rectangle = new Rect(left + 1, VideoTrackBounds.Top, Math.Max(1, right-left-2), VideoTrackBounds.Height);
            var fill = new SolidColorBrush(Color.Parse(selected ? (dark ? "#27436B" : "#E3EDFF") : (dark ? "#343D49" : "#EFF3F8")));
            var outline = selected ? new Pen(new SolidColorBrush(Color.Parse("#5A8DEE")), 2) : stroke;
            context.DrawRectangle(fill, outline, rectangle, 6, 6);
            using (context.PushClip(rectangle))
            {
                Text(context, clip.Name, left + 12, 65, ink, Math.Max(1, right-left-24), 14);
                var duration = TimeSpan.FromTicks(span.EndTicks - span.StartTicks);
                Text(context, $"{duration.TotalSeconds:0.###} s" + (clip.GroupId is null ? "" : "  ⛓"), left+12, 99, ink, Math.Max(1,right-left-24), 12);
            }
            if (vm.Waveforms.TryGetValue(clip.Id, out var waveform))
            {
                var audioRect = new Rect(left + 1, AudioTrackBounds.Top, Math.Max(1, right - left - 2), AudioTrackBounds.Height);
                context.DrawRectangle(new SolidColorBrush(Color.Parse(dark ? "#153946" : "#E2F1F2")),
                    stroke, audioRect, 4, 4);
                using (context.PushClip(audioRect))
                {
                    if (!waveform.HasAudio)
                    {
                        Text(context, vm.Strings["ProjectNoAudioTrack"], left + 8, audioRect.Top + 5,
                            ink, Math.Max(1, audioRect.Width - 16), 11);
                        continue;
                    }
                    var wavePen = new Pen(new SolidColorBrush(Color.Parse(dark ? "#71C8CC" : "#288A91")), 1);
                    var count = waveform.Buckets.Length;
                    for (var b = 0; b < count; b++)
                    {
                        var bucket = waveform.Buckets[b];
                        var x = left + 2 + (right - left - 4) * (b + .5) / count;
                        var gain = clip.Muted ? 0 : clip.Volume;
                        var position = (span.EndTicks - span.StartTicks) * (b + .5) / count;
                        if (clip.FadeInTicks > 0) gain *= Math.Min(1, position / clip.FadeInTicks);
                        if (clip.FadeOutTicks > 0) gain *= Math.Min(1,
                            (span.EndTicks - span.StartTicks - position) / clip.FadeOutTicks);
                        var peak = Math.Clamp(Math.Max(Math.Abs(bucket.Minimum), Math.Abs(bucket.Maximum)) * gain, 0, 1);
                        var middle = audioRect.Center.Y;
                        var amplitude = Math.Max(.5, peak * Math.Max(0, audioRect.Height / 2 - 4));
                        context.DrawLine(wavePen, new(x, middle - amplitude), new(x, middle + amplitude));
                    }
                }
            }
            else if (right - left > 100)
                Text(context, vm.WaveformStatus(clip.Id), left + 8, AudioTrackBounds.Top + 5,
                    ink, Math.Max(1, right - left - 16), 11);
        }
        // Draw the range above opaque clip cards so the selection covers video and audio visibly.
        foreach (var bookmark in vm.Bookmarks)
        {
            if (bookmark.TimelineTicks is not long time) continue;
            var x = X(time); if (x < 16 || x > Bounds.Width - 16) continue;
            context.DrawLine(new Pen(Brushes.MediumPurple, 1), new(x, 34), new(x, Bounds.Height));
            context.FillRectangle(Brushes.MediumPurple, new Rect(x, 35, 7, 7));
        }
        if (vm.RangeStartTicks is long start && vm.RangeEndTicks is long end && end > start)
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(36, 37, 99, 235)),
                new Rect(X(start), 32, Math.Max(0, X(end) - X(start)), Math.Max(0, Bounds.Height - 32)));
        var accent = new Pen(new SolidColorBrush(Color.Parse("#3B82F6")), 2);
        context.DrawLine(accent, new(X(vm.PlayheadTicks), 28), new(X(vm.PlayheadTicks), Bounds.Height));
        context.DrawEllipse(accent.Brush, null, new(X(vm.PlayheadTicks), 30), 4, 4);
        if (vm.RangeStartTicks is long markIn)
            DrawRangeMarker(context, X(markIn), vm.Strings["ProjectRangeStartShort"], true, dark);
        if (vm.RangeEndTicks is long markOut)
            DrawRangeMarker(context, X(markOut), vm.Strings["ProjectRangeEndShort"], false, dark);
        if (_dragging)
        {
            var target = _beforeClip is Guid before ? vm.TimelineClips.First(c => c.ClipId == before).StartTicks : vm.DurationTicks;
            var x = X(target);
            context.DrawLine(new Pen(Brushes.Orange, 3), new(x, 48), new(x, 134));
        }
    }
    private void DrawRangeMarker(DrawingContext context, double x, string label, bool start, bool dark)
    {
        if (x < 16 || x > Bounds.Width - 16) return;
        var brush = new SolidColorBrush(Color.Parse(start ? (dark ? "#5EEAD4" : "#0F766E") : (dark ? "#FDBA74" : "#C2410C")));
        context.DrawLine(new Pen(brush, 2), new(x, 32), new(x, Bounds.Height));
        var left = start ? x : x - 30;
        left = Math.Clamp(left, 16, Math.Max(16, Bounds.Width - 46));
        context.DrawRectangle(brush, null, new Rect(left, 32, 30, 18), 3, 3);
        Text(context, label, left + 5, 34, dark ? Brushes.Black : Brushes.White, 25, 10);
    }
    private static void Text(DrawingContext context, string value, double x, double y, IBrush brush, double width, double size)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush) { MaxTextWidth = width, MaxTextHeight = size * 1.5, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(text, new(x,y));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        HideBookmarkTooltip();
        if (_model is not { CanEditTimeline: true, DurationTicks: > 0 } vm || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var point = e.GetPosition(this);
        _gestureRevision = vm.State.Revision;
        _downX = point.X;
        if (point.Y < 44)
        {
            var bookmark = vm.Bookmarks.FirstOrDefault(b => b.TimelineTicks is long t && Math.Abs(X(t) - point.X) < 6);
            if (bookmark is not null) { vm.SelectedBookmark = bookmark; if (bookmark.TimelineTicks is long tick) vm.Seek(tick); e.Handled = true; return; }
            _seeking = true; vm.BeginPreviewScrub(); vm.Seek(Ticks(point.X));
        }
        else if (point.Y >= VideoTrackBounds.Top && point.Y <= VideoTrackBounds.Bottom)
        {
            var span = vm.TimelineClips.FirstOrDefault(c => Ticks(point.X) >= c.StartTicks && Ticks(point.X) < c.EndTicks);
            if (span is null) return;
            vm.SelectedClip = vm.Clips.First(c => c.Id == span.ClipId);
            _movingClip = span.ClipId;
        }
        else return;
        _pointer = e.Pointer;
        _pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pointer is null)
        {
            var point = e.GetPosition(this);
            var bookmark = _model?.Bookmarks.FirstOrDefault(b => b.TimelineTicks is long t &&
                X(t) >= 16 && X(t) <= Bounds.Width - 16 &&
                new Rect(X(t) - 5, 32, 14, 12).Contains(point));
            if (bookmark is null) HideBookmarkTooltip();
            else
            {
                ToolTip.SetTip(this, bookmark.Name);
                ToolTip.SetIsOpen(this, true);
            }
        }
        if (_pointer != e.Pointer) return;
        UpdateGesture(e.GetPosition(this));
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HideBookmarkTooltip();
    }
    private void HideBookmarkTooltip()
    {
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
    private void UpdateGesture(Point point)
    {
        if (_pointer is null || _model is not { CanEditTimeline: true } vm) return;
        var x = point.X;
        if (_seeking) vm.Seek(Ticks(x));
        if (_movingClip is not null)
        {
            _dragging = Math.Abs(x - _downX) > 5;
            if (!_dragging) { _beforeClip = null; InvalidateVisual(); return; }
            var target = vm.TimelineClips.FirstOrDefault(c => x < (X(c.StartTicks) + X(c.EndTicks)) / 2);
            _beforeClip = target?.ClipId;
            if (_beforeClip is Guid id)
            {
                var index = vm.Clips.ToList().FindIndex(c => c.Id == id);
                var group = vm.Clips[index].GroupId;
                while (group is not null && index > 0 && vm.Clips[index - 1].GroupId == group) index--;
                _beforeClip = vm.Clips[index].Id;
            }
            InvalidateVisual();
        }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer != e.Pointer) return;
        var point = e.GetPosition(this);
        if (_seeking) { UpdateGesture(point); CancelGesture(); return; }
        if (point.X < 16 || point.X > Bounds.Width - 16 || point.Y < VideoTrackBounds.Top ||
            point.Y > Math.Min(VideoTrackBounds.Bottom, Bounds.Height))
        { CancelGesture(); return; }
        UpdateGesture(point);
        var moving = _movingClip; var before = _beforeClip;
        var commit = _dragging && _model?.CanEditTimeline == true && _model.State.Revision == _gestureRevision;
        var clicked = !_dragging && !_seeking ? _movingClip : null;
        CancelGesture();
        if (clicked is Guid clickedId) _ = _model!.SelectClipForPlaybackAsync(clickedId);
        if (commit && moving is Guid id)
        {
            _ = _model!.MoveClipAsync(id, before);
        }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); CancelGesture(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_model?.DurationTicks is not > 0) return;
        CancelGesture();
        var fraction = Math.Clamp((e.GetPosition(this).X - 16) / TrackWidth, 0, 1);
        var anchor = _offset + fraction * VisibleTicks;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        { _zoom = Math.Clamp(_zoom * Math.Pow(1.25, e.Delta.Y), 1, 64); _offset = anchor - fraction * VisibleTicks; }
        else _offset -= (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * VisibleTicks / 10;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _model.DurationTicks - VisibleTicks));
        e.Handled = true; InvalidateVisual();
    }
    private void CancelGesture()
    {
        HideBookmarkTooltip();
        var seeking = _seeking;
        var pointer = _pointer; _pointer = null;
        _movingClip = null; _beforeClip = null; _dragging = false; _seeking = false;
        if (seeking && _model is { } vm) _ = vm.EndPreviewScrubAsync();
        pointer?.Capture(null); InvalidateVisual();
    }
}
