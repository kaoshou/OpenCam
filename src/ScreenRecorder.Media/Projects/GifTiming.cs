using ScreenRecorder.Core.Projects;
namespace ScreenRecorder.Media.Projects;
public static class GifTiming
{
    public static long BoundaryCentiseconds(long frameIndex, int fps)
    {
        if (frameIndex < 0 || fps is not (10 or 15 or 20)) throw new ArgumentOutOfRangeException();
        return checked((long)(((System.Numerics.BigInteger)frameIndex * 100 + fps / 2) / fps));
    }
    public static int FinalDelayCentiseconds(long durationTicks, long frameCount, int fps)
    {
        if (durationTicks <= 0 || frameCount <= 0) throw new ArgumentOutOfRangeException();
        var end = checked((long)(((System.Numerics.BigInteger)durationTicks + 50000) / 100000));
        return checked((int)Math.Max(1, end - BoundaryCentiseconds(frameCount - 1, fps)));
    }
    public static (int Width, int Height) OutputSize(ProjectCanvas canvas, GifExportOptions options)
    {
        if (canvas.Width <= 0 || canvas.Height <= 0 || options.MaximumWidth is not (null or 480 or 640 or 960) ||
            options.FramesPerSecond is not (10 or 15 or 20)) throw new ArgumentOutOfRangeException();
        var width = Math.Min(canvas.Width, options.MaximumWidth ?? canvas.Width);
        return (width, Math.Max(1, checked((int)((long)canvas.Height * width / canvas.Width))));
    }
}
