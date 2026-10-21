using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectGifMediaTests
{
    [MacOsOnlyFact]
    public async Task RenderedGifFramesRespectFractionalTrimCompoundOrderAndOddCanvas()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-render-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var sourcePath = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i",
                "color=red:s=64x36:r=30:d=1", "-f", "lavfi", "-i", "color=blue:s=64x36:r=30:d=1",
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-c:v", "libx264", "-bf", "0", sourcePath]);
            var id = Guid.NewGuid();
            var p = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "gif", Canvas = new(65, 37, new(15, 1)),
                Sessions = ["s"], Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/a.mkv",
                    FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36, VideoCodec = "h264", Timing = new(new(1,1000),0,2000) }],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "blue", InPts = 1200, OutPts = 1533 },
                    new() { Id = Guid.NewGuid(), SourceId = id, Name = "red", InPts = 200, OutPts = 567 }] };
            var plan = ProjectRenderPlan.Create(p);
            var process = new MacProjectMediaProcess(ffmpeg);
            for (var i = 0; i < 2; i++)
            {
                var path = Path.Combine(root.FullName, $"{i}.rgb");
                await using (var source = File.OpenRead(sourcePath))
                await using (var output = File.Create(path))
                    await process.RunAsync(ProjectMediaJob.RenderGifClip(plan, i, new()), [source], output, default);
                var pixels = await File.ReadAllBytesAsync(path);
                Assert.Equal((i == 0 ? 5 : 6) * 65 * 37 * 3, pixels.Length);
                for (var frame = 0; frame < (i == 0 ? 5 : 6); frame++)
                {
                    var center = frame * 65 * 37 * 3 + (18 * 65 + 32) * 3;
                    Assert.InRange(pixels[center + (i == 0 ? 2 : 0)], 230, 255);
                    Assert.InRange(pixels[center + (i == 0 ? 0 : 2)], 0, 20);
                }
            }
        }
        finally { root.Delete(true); }
    }
    [Theory]
    [InlineData(10, 1, 10)]
    [InlineData(15, 1, 7)]
    [InlineData(15, 2, 13)]
    [InlineData(15, 4500, 30000)]
    [InlineData(20, 1, 5)]
    public void TimestampUsesCumulativeCentiseconds(int fps, long frame, long expected)
        => Assert.Equal(expected, GifTiming.BoundaryCentiseconds(frame, fps));

    [Theory]
    [InlineData(1, 1, 15, 1)]
    [InlineData(10000000, 15, 15, 7)]
    [InlineData(10000000, 10, 10, 10)]
    [InlineData(3000000000, 4500, 15, 7)]
    public void LastDelayPreservesTargetDuration(long ticks, long frames, int fps, int expected)
        => Assert.Equal(expected, GifTiming.FinalDelayCentiseconds(ticks, frames, fps));

    [Theory]
    [InlineData(1920, 1080, 480, 480, 270)]
    [InlineData(1920, 1080, 640, 640, 360)]
    [InlineData(1920, 1080, 960, 960, 540)]
    [InlineData(65, 37, 640, 65, 37)]
    [InlineData(1, 1, 480, 1, 1)]
    [InlineData(65, 37, null, 65, 37)]
    public void OutputSizePreservesCanvasAndDoesNotUpscale(int w, int h, int? cap, int expectedW, int expectedH)
    {
        var size = GifTiming.OutputSize(new(w, h, new(30, 1)), new(cap));
        Assert.Equal((expectedW, expectedH), size);
    }

    [MacOsOnlyTheory]
    [InlineData(10, 300, 10)]
    [InlineData(15, 300, 7)]
    [InlineData(20, 300, 5)]
    public async Task RealPaletteEncodingPreservesLongDurationAndLoopsWithoutAudio(int fps, int seconds, int lastDelay)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-media-");
        try
        {
            var rawPath = Path.Combine(root.FullName, "frames.rgb");
            // Small canvas keeps the long-duration regression inexpensive; frames stream to disk.
            await using (var raw = File.Create(rawPath))
                for (var n = 0; n < fps * seconds; n++)
                    await raw.WriteAsync(n % 2 == 0 ? new byte[] {255,0,0,0,255,0,0,0,255} : new byte[] {0,0,255,255,0,0,0,255,0});
            var process = new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!);
            var palettePath = Path.Combine(root.FullName, "palette.png");
            var gifPath = Path.Combine(root.FullName, "out.gif");
            await using (var raw = File.OpenRead(rawPath))
            await using (var palette = File.Create(palettePath))
                await process.RunAsync(ProjectMediaJob.AnalyzeGifPalette(3, 1, fps), [raw], palette, default);
            await using (var raw = File.OpenRead(rawPath))
            await using (var palette = File.OpenRead(palettePath))
            await using (var gif = File.Create(gifPath))
                await process.RunAsync(ProjectMediaJob.EncodeGif(3, 1, fps, lastDelay), [raw, palette], gif, default);
            await using (var gif = File.OpenRead(gifPath))
            {
                var info = GifStructureReader.Read(gif);
                Assert.Equal(3, info.Width); Assert.Equal(1, info.Height);
                Assert.Equal(fps * seconds, info.FrameCount); Assert.Equal(30000, info.DurationCentiseconds);
                Assert.True(info.InfiniteLoop);
                var inspection = await process.RunAsync(ProjectMediaJob.InspectGif(), [gif], null, default);
                using var json = System.Text.Json.JsonDocument.Parse(inspection.Output);
                var stream = Assert.Single(json.RootElement.GetProperty("streams").EnumerateArray());
                Assert.Equal("gif", stream.GetProperty("codec_name").GetString());
                Assert.Equal((fps * seconds).ToString(), stream.GetProperty("nb_read_frames").GetString());
            }
            var bytes = await File.ReadAllBytesAsync(gifPath);
            Assert.Throws<InvalidDataException>(() => GifStructureReader.Read(new MemoryStream(bytes[..^1])));
        }
        finally { root.Delete(true); }
    }
}
