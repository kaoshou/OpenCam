// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class PreviewBitmapBufferTests
{
    [AvaloniaFact]
    public void SameSizeFramesReuseBitmapAndDifferentSizeReplacesIt()
    {
        using var buffer = new PreviewBitmapBuffer();
        ProjectFrameReply Frame(int width, int height, byte value) => new(0, 0, Guid.NewGuid(),
            Enumerable.Repeat(value, width * height * 4).ToArray()) { PixelWidth = width, PixelHeight = height };
        var first = buffer.Update(Frame(4, 2, 10));
        var next = buffer.Update(Frame(4, 2, 20));
        Assert.Same(first, next);
        // The headless drawing stub does not retain pixels between locks; the real-Skia
        // verification run exercises actual framebuffer contents as well as allocation reuse.
        if (Environment.GetEnvironmentVariable("OPENCAM_REAL_UI_RENDER") == "1")
            using (var pixels = next.Lock()) Assert.Equal(20, System.Runtime.InteropServices.Marshal.ReadByte(pixels.Address));
        var resized = buffer.Update(Frame(2, 2, 30));
        Assert.NotSame(first, resized); Assert.Equal(new PixelSize(2, 2), resized.PixelSize);
        Assert.Throws<InvalidDataException>(() => buffer.Update(new(0, 0, Guid.NewGuid(), [1])));
        buffer.Clear(); Assert.Null(buffer.Bitmap);
    }
}
