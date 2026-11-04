// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

/// <summary>UI-thread-owned pixel buffer; avoids a native bitmap allocation for every preview frame.</summary>
internal sealed class PreviewBitmapBuffer : IDisposable
{
    public WriteableBitmap? Bitmap { get; private set; }
    public WriteableBitmap Update(ProjectFrameReply frame)
    {
        if (!frame.HasValidPixels) throw new InvalidDataException("Invalid preview pixels.");
        var size = new PixelSize(frame.PixelWidth, frame.PixelHeight);
        if (Bitmap?.PixelSize != size)
        {
            var replacement = new WriteableBitmap(size, new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            Bitmap?.Dispose(); Bitmap = replacement;
        }
        using (var locked = Bitmap.Lock())
            for (var y = 0; y < frame.PixelHeight; y++)
                System.Runtime.InteropServices.Marshal.Copy(frame.Rgba!, y * frame.PixelWidth * 4,
                    locked.Address + y * locked.RowBytes, frame.PixelWidth * 4);
        return Bitmap;
    }
    public void Clear() { Bitmap?.Dispose(); Bitmap = null; }
    public void Dispose() => Clear();
}
