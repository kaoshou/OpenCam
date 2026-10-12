// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects.Editor;

/// <summary>A virtualizable card. Owns and releases its bitmap when detached/recycled.</summary>
public sealed class ProjectClipCard : UserControl
{
    private readonly Image _image = new() { Width = 64, Height = 40, Stretch = Stretch.Uniform };
    private readonly TextBlock _name = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _duration = new() { FontSize = 11, Foreground = Brushes.SlateGray };
    private readonly TextBlock _ordinal = new() { FontSize = 11, Foreground = Brushes.SlateGray, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly Grid _grid = new();
    private readonly Border _groupBorder = new();
    private readonly Border _thumbnail;
    private WriteableBitmap? _bitmap;
    private byte[]? _pixels;
    private ProjectWorkspaceViewModel? _model;

    public ProjectClipCard()
    {
        _thumbnail = new Border { Background = new SolidColorBrush(Color.Parse("#11161F")), CornerRadius = new(4),
            Width = 64, Height = 40, Child = _image, ClipToBounds = true };
        _grid.Children.Add(_thumbnail); _grid.Children.Add(_name); _grid.Children.Add(_duration); _grid.Children.Add(_ordinal);
        _groupBorder.Child = _grid;
        Content = _groupBorder;
        _name.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _duration.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        AttachedToVisualTree += (_, _) => {
            _model = (TopLevel.GetTopLevel(this) as Window)?.DataContext as ProjectWorkspaceViewModel;
            if (_model is not null) _model.PropertyChanged += Changed;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => {
            if (_model is not null) _model.PropertyChanged -= Changed;
            _model = null; Release();
        };
        DataContextChanged += (_, _) => Refresh();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectWorkspaceViewModel.Thumbnails) or nameof(ProjectWorkspaceViewModel.TimelineClips)
            or nameof(ProjectWorkspaceViewModel.IsClipListCompact)) Refresh();
    }
    private void Release() { _image.Source = null; _bitmap?.Dispose(); _bitmap = null; _pixels = null; }
    private void Refresh()
    {
        _groupBorder.Background = Brushes.Transparent;
        _groupBorder.BorderThickness = new(0);
        var row = this.FindAncestorOfType<ListBoxItem>();
        if (row is not null)
        {
            row.Margin = new(0, 2);
            row.BorderThickness = new(1);
            row.CornerRadius = new(7);
        }
        ToolTip.SetTip(this, null);
        if (DataContext is not ProjectClip clip) { Release(); return; }
        _name.Text = clip.Name;
        var span = _model?.TimelineClips.FirstOrDefault(c => c.ClipId == clip.Id);
        var compact = _model?.IsClipListCompact == true;
        var duration = span is null ? (TimeSpan?)null : TimeSpan.FromTicks(span.EndTicks - span.StartTicks);
        var time = duration?.ToString(duration?.TotalHours >= 1 ? @"hh\:mm\:ss\.f" : @"mm\:ss\.f") ?? "—";
        var number = ((_model?.Clips.IndexOf(clip) ?? -1) + 1).ToString("00");
        Height = compact ? 32 : 64;
        _grid.Margin = compact ? new(8, 2) : new(8);
        _grid.ColumnDefinitions = new(compact ? "24,*,58" : "64,10,*");
        _grid.RowDefinitions = new(compact ? "*" : "*,*");
        _image.IsVisible = _thumbnail.IsVisible = !compact;
        _ordinal.IsVisible = compact;
        _ordinal.Text = number;
        Grid.SetRowSpan(_thumbnail, compact ? 1 : 2);
        Grid.SetColumn(_name, compact ? 1 : 2);
        Grid.SetColumn(_duration, 2);
        Grid.SetRow(_duration, compact ? 0 : 1);
        _duration.Text = compact ? time : $"{number} · {time}";
        if (clip.GroupId is Guid group && _model is not null)
        {
            var groups = _model.Clips.Where(c => c.GroupId is not null).Select(c => c.GroupId).Distinct().ToList();
            var ordinal = groups.IndexOf(group) + 1;
            var colors = new[] { "#5785C4", "#6A9C85", "#A180BB", "#B58C58" };
            var color = Color.Parse(colors[(ordinal - 1) % colors.Length]);
            _groupBorder.BorderBrush = new SolidColorBrush(color);
            _groupBorder.Background = new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B));
            var index = _model.Clips.IndexOf(clip);
            var first = index == 0 || _model.Clips[index - 1].GroupId != group;
            var last = index == _model.Clips.Count - 1 || _model.Clips[index + 1].GroupId != group;
            if (row is not null)
            {
                row.Margin = new(0, first ? 2 : 0, 0, last ? 2 : 0);
                row.BorderThickness = new(0);
                row.CornerRadius = new(first ? 6 : 0, first ? 6 : 0, last ? 6 : 0, last ? 6 : 0);
            }
            _groupBorder.BorderThickness = new(3, first ? 1 : 0, 1, last ? 1 : 0);
            _groupBorder.CornerRadius = new(first ? 6 : 0, first ? 6 : 0, last ? 6 : 0, last ? 6 : 0);
            var label = $"{_model.Strings["ProjectGroupLabel"]} {ordinal}";
            _duration.Text = compact ? $"G{ordinal} · {time}" : $"{label} · {time}";
            ToolTip.SetTip(this, label);
            if (compact) _grid.ColumnDefinitions = new("24,*,88");
        }
        _duration.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
        if (compact) { Release(); return; }
        var bytes = _model?.Thumbnails.GetValueOrDefault(clip.Id)?.Rgba;
        if (ReferenceEquals(bytes, _pixels)) return;
        Release();
        if (bytes is null) return;
        _bitmap = new(new(512, 288), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using (var locked = _bitmap.Lock())
            for (var y = 0; y < 288; y++) Marshal.Copy(bytes, y * 2048, locked.Address + y * locked.RowBytes, 2048);
        _pixels = bytes; _image.Source = _bitmap;
    }
}
