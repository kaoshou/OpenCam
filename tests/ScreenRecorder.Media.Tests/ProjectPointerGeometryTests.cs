// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPointerGeometryTests
{
    private static ProjectSource Source(int width, int height) => new() {
        Id = Guid.NewGuid(), Width = width, Height = height, SessionId = "s", RelativePath = "sources/source.mkv",
        FileSize = 100, Sha256 = new string('a', 64), VideoCodec = "ffv1", Timing = new(new(1, 1000), 0, 1000) };

    private static (Func<PointerSample?, PointerSample?> Map, double Scale) Geometry(ProjectSource source, ProjectClip clip,
        ProjectCanvas canvas, int width, int height, bool preview = false, bool gif = false)
    {
        var geometry = ProjectPointerGeometry.Create(source, clip, canvas, width, height, preview, gif);
        return (geometry.Map, geometry.RasterScale);
    }

    [Fact]
    public void FinalPreviewLetterboxPreservesHotspotAndSourceRelativeSize()
    {
        var geometry = Geometry(Source(1920, 1080), new(), new(1920, 1080, new(30, 1)), 512, 288, preview: true);
        var point = geometry.Map(new(17, 960, 540, true, 3))!.Value;
        Assert.Equal(new PointerSample(17, 256, 144, true, 3), point);
        Assert.Equal(1, geometry.Scale, 12);
        var square = Geometry(Source(1000, 1000), new(), new(1920, 1080, new(30, 1)), 512, 288, preview: true);
        Assert.Equal(112, square.Map(new(0, 0, 0, true, 0))!.Value.X, 12);
        Assert.Equal(1, square.Scale, 12);
    }

    [Fact]
    public void CroppedSourceIntervalsAreHalfOpenAndHiddenSamplesDoNotRender()
    {
        var geometry = Geometry(Source(640, 480), new() { Crop = 10 }, new(640, 480, new(30, 1)), 640, 480);
        Assert.Null(geometry.Map(new(0, 63.99, 240, true, 0)));
        Assert.Equal(0, geometry.Map(new(0, 64, 240, true, 0))!.Value.X);
        Assert.Null(geometry.Map(new(0, 576, 240, true, 0)));
        Assert.Null(geometry.Map(new(0, 320, 432, true, 0)));
        Assert.Null(geometry.Map(new(0, 320, 240, false, 0)));
        Assert.Null(geometry.Map(null));
        Assert.Equal(1.25, geometry.Scale, 12);
    }

    [Fact]
    public void FitRoundsToNearestEvenDimensionBeforeCenterPadding()
    {
        // 334 * 640 / 500 = 427.52: FFmpeg fits to 428, rather than 426.
        var geometry = Geometry(Source(500, 334), new(), new(640, 480, new(30, 1)), 640, 480);
        var point = geometry.Map(new(0, 250, 0, true, 0))!.Value;
        Assert.Equal(320, point.X); Assert.Equal(26, point.Y);
        Assert.Equal(428d / 480, geometry.Scale, 12);
    }

    [Fact]
    public void OddSourceCropCentersUseTiesToEvenRounding()
    {
        var geometry = Geometry(Source(641, 479), new() { Crop = 11 }, new(498, 372, new(30, 1)), 498, 372);
        var point = geometry.Map(new(0, 72, 54, true, 0))!.Value;
        Assert.Equal(0, point.X); Assert.Equal(0, point.Y);
        Assert.Null(geometry.Map(new(0, 71.99, 54, true, 0)));
    }

    [Fact]
    public void ZoomAndNegativePlacementTruncateOnTheSamePixelGridAsOverlay()
    {
        var geometry = Geometry(Source(640, 480), new() { Scale = 1.333, PositionX = 1.1, PositionY = -1.1 },
            new(640, 480, new(30, 1)), 640, 480);
        var point = geometry.Map(new(0, 320, 240, true, 0))!.Value;
        Assert.Equal(328, point.X); Assert.Equal(235, point.Y);
        Assert.Equal(638d / 480, geometry.Scale, 12);
        Assert.Null(geometry.Map(new(0, 0, 0, true, 0)));
    }

    [Fact]
    public void LargePreviewUsesTheBoundedCompositionCanvas()
    {
        var geometry = Geometry(Source(4096, 2160), new(), new(8192, 8192, new(30, 1)), 512, 288, preview: true);
        var point = geometry.Map(new(0, 2048, 1080, true, 0))!.Value;
        Assert.Equal(256, point.X); Assert.Equal(144, point.Y);
        Assert.Equal(1080d / 2048, geometry.Scale, 12);
    }

    [MacOsOnlyTheory]
    [InlineData(0, 1, 0, 0, "preview")]
    [InlineData(11, 1.333, 1.1, -1.1, "preview")]
    [InlineData(40, 1.5, 8, -5, "mp4")]
    [InlineData(11, 1.333, 1.1, -1.1, "gif")]
    public async Task MappedTipMatchesTheActualFfmpegSourceMarker(double crop, double scale, double x, double y, string mode)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pointer-geometry-");
        try
        {
            var source = Source(641, 479);
            source = source with { Pointer = new($"pointers/{source.Id:N}.ocpt", 485, new string('b', 64), 0, true) };
            var raw = new byte[source.Width * source.Height * 4];
            for (var i = 3; i < raw.Length; i += 4) raw[i] = 255;
            for (var py = 246; py <= 254; py++)
            for (var px = 346; px <= 354; px++) raw[(py * source.Width + px) * 4] = 255;
            var rawPath = Path.Combine(root.FullName, "source.rgba");
            await File.WriteAllBytesAsync(rawPath, raw);
            var path = Path.Combine(root.FullName, "source.mkv");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "rawvideo", "-pixel_format", "rgba",
                "-video_size", "641x479", "-framerate", "1", "-i", rawPath, "-frames:v", "1", "-c:v", "ffv1", "-pix_fmt", "bgr0", path]);
            var clip = new ProjectClip { Id = Guid.NewGuid(), SourceId = source.Id, Name = "marker", InPts = 0, OutPts = 1000,
                Crop = crop, Scale = scale, PositionX = x, PositionY = y };
            var canvas = new ProjectCanvas(mode == "gif" ? 801 : 800, mode == "gif" ? 601 : 600, new(10, 1));
            var project = new RecordingProject { SchemaVersion = 5, ProjectId = Guid.NewGuid(), Name = "marker", Sources = [source],
                Clips = [clip], Sessions = ["s"], Canvas = canvas, PointerSettings = new(ProjectPointerStyle.Hidden) };
            var plan = ProjectRenderPlan.Create(project);
            var process = new MacProjectMediaProcess(ffmpeg);
            byte[] pixels; int width, height, bytesPerPixel;
            using var input = File.OpenRead(path);
            if (mode == "preview")
            {
                (width, height, bytesPerPixel) = (512, 288, 4);
                pixels = (await process.RunAsync(ProjectMediaJob.PreviewStill(plan, 0, 0, width, height), [input], null, default)).Output;
            }
            else if (mode == "gif")
            {
                var options = new GifExportOptions(FramesPerSecond: 10, MaximumWidth: 480);
                (width, height) = GifTiming.OutputSize(canvas, options); bytesPerPixel = 3;
                using var output = new MemoryStream();
                await process.RunStreamingAsync(ProjectMediaJob.RenderGifClip(plan, 0, options), input,
                    (stream, ct) => stream.CopyToAsync(output, ct), default);
                pixels = output.ToArray();
            }
            else
            {
                (width, height, bytesPerPixel) = (800, 600, 4);
                var encoded = Path.Combine(root.FullName, "encoded.h264");
                using (var output = File.Create(encoded))
                    await process.RunAsync(ProjectMediaJob.EncodeClipVideo(plan, 0), [input], output, default);
                pixels = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", encoded, "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
            }
            var geometry = Geometry(source, clip, canvas, width, height, preview: mode == "preview", gif: mode == "gif");
            var point = geometry.Map(new(0, 350.5, 250.5, true, 0))!.Value;
            var marked = new List<(double X, double Y)>();
            for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var offset = (py * width + px) * bytesPerPixel;
                if (pixels[offset] > 160 && pixels[offset + 1] < 50 && pixels[offset + 2] < 50) marked.Add((px + .5, py + .5));
            }
            Assert.NotEmpty(marked);
            Assert.InRange(Math.Abs(marked.Average(p => p.X) - point.X), 0, 1.1);
            Assert.InRange(Math.Abs(marked.Average(p => p.Y) - point.Y), 0, 1.1);
            var overlay = new byte[width * height * 4];
            PointerRasterizer.Composite(overlay, width, height, point, new(ProjectPointerStyle.Dot, 200), geometry.Scale);
            var tip = ((int)point.Y * width + (int)point.X) * 4;
            Assert.True(overlay[tip + 3] > 0);
        }
        finally { root.Delete(true); }
    }
}
