// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>
/// Source-pixel hotspot placement for the editable-source RGBA composition in ComposeClipVideo.
/// Create once per clip/output size; Map is allocation-free. Crop and output bounds are half-open.
/// </summary>
public sealed class ProjectPointerGeometry
{
    private readonly int _cropX, _cropY, _cropWidth, _cropHeight, _canvasWidth, _canvasHeight, _outputWidth, _outputHeight;
    private readonly double _sourceScaleX, _sourceScaleY, _offsetX, _offsetY, _finalScaleX, _finalScaleY;
    private readonly int _finalPadX, _finalPadY;

    /// <summary>
    /// Multiplier for PointerRasterizer's output-height/1080 unit. The visual size is source-relative:
    /// source-height/1080, followed by crop fitting, clip zoom, and final output resizing. Letterboxing
    /// does not enlarge the pointer to the padded height. This is shared by preview, MP4, and GIF.
    /// </summary>
    public double RasterScale { get; }

    private ProjectPointerGeometry(int sourceHeight, int cropX, int cropY, int cropWidth, int cropHeight,
        int canvasWidth, int canvasHeight, int fitWidth, int fitHeight, int zoomWidth, int zoomHeight,
        int overlayX, int overlayY, int outputWidth, int outputHeight, int finalWidth, int finalHeight)
    {
        (_cropX, _cropY, _cropWidth, _cropHeight) = (cropX, cropY, cropWidth, cropHeight);
        (_canvasWidth, _canvasHeight, _outputWidth, _outputHeight) = (canvasWidth, canvasHeight, outputWidth, outputHeight);
        _sourceScaleX = (double)fitWidth / cropWidth * zoomWidth / canvasWidth;
        _sourceScaleY = (double)fitHeight / cropHeight * zoomHeight / canvasHeight;
        // RGBA pad truncates its centered offsets, then zoom scales the whole padded canvas.
        _offsetX = (canvasWidth - fitWidth) / 2 * ((double)zoomWidth / canvasWidth) + overlayX;
        _offsetY = (canvasHeight - fitHeight) / 2 * ((double)zoomHeight / canvasHeight) + overlayY;
        _finalScaleX = (double)finalWidth / canvasWidth;
        _finalScaleY = (double)finalHeight / canvasHeight;
        _finalPadX = (outputWidth - finalWidth) / 2;
        _finalPadY = (outputHeight - finalHeight) / 2;
        RasterScale = sourceHeight * _sourceScaleY * _finalScaleY / outputHeight;
    }

    public static ProjectPointerGeometry Create(ProjectSource source, ProjectClip clip, ProjectCanvas canvas,
        int outputWidth, int outputHeight, bool preview = false, bool gif = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(canvas);
        if (!Dimensions(source.Width, source.Height) || !Dimensions(canvas.Width, canvas.Height) ||
            !Dimensions(outputWidth, outputHeight) || (long)outputWidth * outputHeight * 4 > ProjectMediaJob.MaximumFrameBytes ||
            !Range(clip.Crop, 0, 40) || !Range(clip.Scale, 1, 2) ||
            !Range(clip.PositionX, -50, 50) || !Range(clip.PositionY, -50, 50) || preview && gif)
            throw new InvalidDataException("Invalid pointer render geometry.");
        if (!preview && !gif && (canvas.Width % 2 != 0 || canvas.Height % 2 != 0 ||
            canvas.Width > 4096 || canvas.Height > 4096))
            throw new InvalidDataException("H.264 pointer composition requires an even canvas up to 4096 pixels.");

        var width = canvas.Width; var height = canvas.Height;
        if (preview && (long)width * height * 4 > ProjectMediaJob.MaximumFrameBytes)
        {
            var ratio = 2048d / Math.Max(width, height);
            width = Math.Max(2, (int)(width * ratio));
            height = Math.Max(2, (int)(height * ratio));
        }
        // Preserve the exact double expression emitted by ComposeClipVideo, including its
        // floating-point truncation at per-edge crop and zoom boundaries.
        var fraction = 1 - clip.Crop / 100 * 2;
        var cropWidth = gif ? Math.Max(1, (int)(source.Width * fraction)) : (int)(source.Width * fraction / 2) * 2;
        var cropHeight = gif ? Math.Max(1, (int)(source.Height * fraction)) : (int)(source.Height * fraction / 2) * 2;
        if (cropWidth <= 0 || cropHeight <= 0) throw new InvalidDataException("Pointer crop has no retained pixels.");
        // vf_crop.c normalizes its default center with lrint (nearest, ties to even).
        var cropX = (int)Math.Round((source.Width - cropWidth) / 2d, MidpointRounding.ToEven);
        var cropY = (int)Math.Round((source.Height - cropHeight) / 2d, MidpointRounding.ToEven);
        var fit = Fit(cropWidth, cropHeight, width, height, gif ? 1 : 2);
        var zoomWidth = gif ? Math.Max(1, (int)(width * clip.Scale)) : (int)(width * clip.Scale / 2) * 2;
        var zoomHeight = gif ? Math.Max(1, (int)(height * clip.Scale)) : (int)(height * clip.Scale / 2) * 2;
        if (zoomWidth <= 0 || zoomHeight <= 0) throw new InvalidDataException("Pointer zoom has no output pixels.");
        // vf_overlay.c casts the expression to int; RGB adds no chroma-grid rounding.
        var overlayX = (int)((width - zoomWidth) / 2d + width * (clip.PositionX / 100));
        var overlayY = (int)((height - zoomHeight) / 2d + height * (clip.PositionY / 100));
        var final = preview ? Fit(width, height, outputWidth, outputHeight, 1) : (outputWidth, outputHeight);
        return new(source.Height, cropX, cropY, cropWidth, cropHeight, width, height, fit.Width, fit.Height,
            zoomWidth, zoomHeight, overlayX, overlayY, outputWidth, outputHeight, final.Item1, final.Item2);
    }

    public PointerSample? Map(PointerSample? point)
    {
        if (point is not { Visible: true } value) return null;
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
        if (value.X < _cropX || value.Y < _cropY || value.X >= _cropX + _cropWidth || value.Y >= _cropY + _cropHeight)
            return null;
        var x = (value.X - _cropX) * _sourceScaleX + _offsetX;
        var y = (value.Y - _cropY) * _sourceScaleY + _offsetY;
        // A translated/zoomed source can be clipped before final preview padding is added.
        if (x < 0 || y < 0 || x >= _canvasWidth || y >= _canvasHeight) return null;
        x = x * _finalScaleX + _finalPadX;
        y = y * _finalScaleY + _finalPadY;
        return x < 0 || y < 0 || x >= _outputWidth || y >= _outputHeight ? null : value with { X = x, Y = y };
    }

    private static bool Dimensions(int width, int height) => width is > 0 and <= 16384 && height is > 0 and <= 16384;
    private static bool Range(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

    private static (int Width, int Height) Fit(int inputWidth, int inputHeight, int width, int height, int divisible)
    {
        // FFmpeg 7.1 scale_eval.c ff_scale_adjust_dimensions first rounds to the nearest
        // divisible multiple with av_rescale, then bounds/rounds down the requested box.
        // https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavfilter/scale_eval.c
        static int Rescale(int value, int numerator, int denominator) =>
            checked((int)(((long)value * numerator + denominator / 2) / denominator));
        var fittedWidth = Math.Min(width, Rescale(height, inputWidth, inputHeight * divisible) * divisible) / divisible * divisible;
        var fittedHeight = Math.Min(height, Rescale(width, inputHeight, inputWidth * divisible) * divisible) / divisible * divisible;
        if (fittedWidth <= 0 || fittedHeight <= 0) throw new InvalidDataException("Pointer aspect fit has no output pixels.");
        return (fittedWidth, fittedHeight);
    }
}
