// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using Serilog;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    public record TeachingPointerStyleOption(TeachingPointerStyle Style, string DisplayName, Bitmap? Thumbnail);
    public record TeachingPointerSizeOption(TeachingPointerSize Size, string DisplayName);
    private static readonly ConcurrentDictionary<TeachingPointerStyle, Lazy<Bitmap>> PointerThumbnails = new();
    public ObservableCollection<TeachingPointerStyleOption> AvailableTeachingPointerStyles { get; } = new();
    public ObservableCollection<TeachingPointerSizeOption> AvailableTeachingPointerSizes { get; } = new();
    [ObservableProperty] private TeachingPointerStyleOption? _selectedPresentationPointerStyle;
    [ObservableProperty] private TeachingPointerSizeOption? _selectedPresentationPointerSize;
    private bool _refreshingTeachingPointerOptions;
    public bool ShowPresentationPointerOptions => SelectedCursorEffect?.Mode == CursorEffectMode.PresentationStick;
    private TeachingPointerOptions CurrentPresentationPointer => new(
        SelectedPresentationPointerStyle?.Style ?? TeachingPointerStyle.WarmIvory,
        SelectedPresentationPointerSize?.Size ?? TeachingPointerSize.Standard);

    partial void OnSelectedPresentationPointerStyleChanged(TeachingPointerStyleOption? value)
    {
        if (value is not null) PersistUserSettings();
    }
    partial void OnSelectedPresentationPointerSizeChanged(TeachingPointerSizeOption? value)
    {
        if (value is not null) PersistUserSettings();
    }

    private void RefreshTeachingPointerOptions(TeachingPointerOptions? requested = null)
    {
        var options = TeachingPointerOptions.Normalize(requested ?? CurrentPresentationPointer);
        var previous = _refreshingTeachingPointerOptions;
        _refreshingTeachingPointerOptions = true;
        try
        {
            AvailableTeachingPointerStyles.Clear();
            foreach(var style in Enum.GetValues<TeachingPointerStyle>())
            {
                Bitmap? image = null;
                Lazy<Bitmap>? thumbnail = null;
                try
                {
                    thumbnail = PointerThumbnails.GetOrAdd(style, key => new(() =>
                    {
                        using var stream = TeachingPointerArtwork.OpenPng(key);
                        return new Bitmap(stream);
                    }));
                    image = thumbnail.Value;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    // Services may not exist yet when a non-window consumer
                    // creates a view model. Do not cache that transient failure
                    // forever, nor remove a replacement created by another thread.
                    if (thumbnail is not null)
                        ((ICollection<KeyValuePair<TeachingPointerStyle, Lazy<Bitmap>>>)PointerThumbnails)
                            .Remove(new(style, thumbnail));
                    // A decorative preview must never prevent ordinary recording.
                    // The overlay independently validates its artwork before cursor suppression.
                    Log.Warning("Teaching pointer thumbnail unavailable: {Style}, {ErrorType}",
                        style, error.GetType().Name);
                }
                AvailableTeachingPointerStyles.Add(new(style,Strings["TeachingPointer"+style],image));
            }
            AvailableTeachingPointerSizes.Clear();
            foreach(var size in Enum.GetValues<TeachingPointerSize>())
                AvailableTeachingPointerSizes.Add(new(size,Strings["TeachingPointerSize"+size]));
            SelectedPresentationPointerStyle = AvailableTeachingPointerStyles.Single(x=>x.Style==options.Style);
            SelectedPresentationPointerSize = AvailableTeachingPointerSizes.Single(x=>x.Size==options.Size);
        }
        finally { _refreshingTeachingPointerOptions=previous; }
        OnPropertyChanged(nameof(ShowPresentationPointerOptions));
    }
}
