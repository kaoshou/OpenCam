namespace ScreenRecorder.Media.Projects;
public sealed partial class ProjectMediaJob
{
    public static ProjectMediaJob RenderGifClip(ProjectRenderPlan plan, int index, ScreenRecorder.Core.Projects.GifExportOptions options)
    {
        GifTiming.OutputSize(plan.Canvas, options);
        if (plan.Canvas.Fps.Numerator != options.FramesPerSecond || plan.Canvas.Fps.Denominator != 1)
            throw new InvalidDataException("GIF render plan must use the selected sampling rate.");
        return ComposeClipVideo(plan, index, 0, false, options);
    }
    private static string[] GifRawInput(int width, int height, int fps)
    {
        if (width is <= 0 or > 16384 || height is <= 0 or > 16384 || fps is not (10 or 15 or 20))
            throw new ArgumentOutOfRangeException();
        return ["-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "fd,pipe",
            "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{width}x{height}",
            "-framerate", fps.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", "fd:"];
    }
    public static ProjectMediaJob AnalyzeGifPalette(int width, int height, int fps)
    {
        return new(0, new(1, 1), 0, 0) { RequiresOutput = true, IsLongRunning = true, OutputLimit = 65536,
            _exportArguments = [..GifRawInput(width, height, fps), "-vf", "palettegen=reserve_transparent=0",
                "-an", "-frames:v", "1", "-threads", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1"] };
    }
    public static ProjectMediaJob EncodeGif(int width, int height, int fps, int finalDelayCentiseconds)
    {
        if (finalDelayCentiseconds is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(finalDelayCentiseconds));
        return new(0, new(1, 1), 0, 0) { InputCount = 2, RequiresOutput = true, IsLongRunning = true, OutputLimit = long.MaxValue,
            _exportArguments = [..GifRawInput(width, height, fps), "-protocol_whitelist", "fd,pipe",
                "-f", "png_pipe", "-fd", "3", "-i", "fd:", "-filter_complex_threads", "1",
                "-filter_complex", $"[0:v]settb=1/100,setpts=floor((N*100+{fps / 2})/{fps})[v];[v][1:v]paletteuse=dither=sierra2_4a[out]",
                "-map", "[out]", "-an", "-sn", "-dn", "-fps_mode", "passthrough", "-enc_time_base", "1/100",
                "-c:v", "gif", "-loop", "0", "-final_delay", finalDelayCentiseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-f", "gif", "pipe:1"] };
    }
    public static ProjectMediaJob InspectGif()
        => new(0, new(1, 1), 0, 0) { IsProbe = true, IsLongRunning = true, OutputLimit = MaximumDiagnosticBytes,
            _exportArguments = ["-v", "error", "-protocol_whitelist", "fd,pipe", "-ignore_loop", "1", "-count_frames",
                "-show_entries", "stream=codec_type,codec_name,width,height,nb_read_frames", "-of", "json", "fd:"] };
}
