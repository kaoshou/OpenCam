// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects.Editor;

/// <summary>A virtualizable card. Owns and releases its bitmap when detached/recycled.</summary>
public sealed class ProjectClipCard : UserControl
{
    private readonly Image _image = new() { Width = 64, Height = 40, Stretch = Stretch.Uniform };
    private readonly TextBlock _name = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _duration = new() { FontSize = 11, Foreground = Brushes.SlateGray };
    private WriteableBitmap? _bitmap;
    private byte[]? _pixels;
    private ProjectWorkspaceViewModel? _model;

    public ProjectClipCard()
    {
        var grid = new Grid { ColumnDefinitions = new("64,10,*"), Margin = new(8) };
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#11161F")), CornerRadius = new(4),
            Width = 64, Height = 40, Child = _image, ClipToBounds = true });
        var text = new StackPanel { Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { _name, _duration } };
        Grid.SetColumn(text, 2); grid.Children.Add(text); Content = grid;
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
        if (e.PropertyName is nameof(ProjectWorkspaceViewModel.Thumbnails) or nameof(ProjectWorkspaceViewModel.TimelineClips)) Refresh();
    }
    private void Release() { _image.Source = null; _bitmap?.Dispose(); _bitmap = null; _pixels = null; }
    private void Refresh()
    {
        if (DataContext is not ProjectClip clip) { Release(); return; }
        _name.Text = clip.Name;
        var span = _model?.TimelineClips.FirstOrDefault(c => c.ClipId == clip.Id);
        _duration.Text = span is null ? "—" : TimeSpan.FromTicks(span.EndTicks - span.StartTicks).ToString(@"mm\:ss\.fff");
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
