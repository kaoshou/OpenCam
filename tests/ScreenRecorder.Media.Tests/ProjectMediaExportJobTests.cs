// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMediaExportJobTests
{
    [MacOsOnlyFact]
    public async Task LateAudioRetainsOffsetFromVideoOriginInStereoRender()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-export-late-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=1",
                "-f", "lavfi", "-i", "aevalsrc=0.1:s=48000:d=0.8", "-vf", "setpts=PTS+2/TB", "-af", "asetpts=PTS+2.2/TB",
                "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le", path]);
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "late", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                    FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_f32le",
                    Timing = new(new(1, 1000), 2000, 1000) }], Clips = [new() { Id = Guid.NewGuid(), SourceId = id,
                    Name = "late", InPts = 2000, OutPts = 3000 }] };
            await using var source = File.OpenRead(path);
            var outputPath = Path.Combine(root.FullName, "samples.pcm");
            await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite))
                await new MacProjectMediaProcess(ffmpeg).RunAsync(ProjectMediaJob.RenderClipAudio(ProjectRenderPlan.Create(project), 0), [source], output, default);
            var samples = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(48000 * 8, samples.Length);
            var first = Enumerable.Range(0, 48000).First(i => Math.Abs(BitConverter.ToSingle(samples, i * 8)) > .001);
            Assert.InRange(first, 9599, 9601); // 200 ms belongs to video, not silence discarded by independent STARTPTS.
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task VideoPositionAndScaleAffectDecodedOutputWithoutStretchingCanvas()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-export-transform-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i",
                "color=red:s=64x36:r=30:d=0.1,drawbox=x=32:y=0:w=32:h=36:c=blue:t=fill", "-c:v", "libx264", path]);
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "geometry", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                    FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36, VideoCodec = "h264",
                    Timing = new(new(1, 1000), 0, 100) }], Clips = [new() { Id = Guid.NewGuid(), SourceId = id,
                    Name = "shift", InPts = 0, OutPts = 100, PositionX = 50 }] };
            await using var source = File.OpenRead(path);
            foreach (var scale in new[] { 1d, 2d })
            {
                var plan = ProjectRenderPlan.Create(project with { Clips = [project.Clips[0] with { Scale = scale }] });
                var h264 = Path.Combine(root.FullName, $"scaled-{scale}.h264");
                await using (var output = new FileStream(h264, FileMode.CreateNew, FileAccess.ReadWrite))
                    await new MacProjectMediaProcess(ffmpeg).RunAsync(ProjectMediaJob.EncodeClipVideo(plan, 0), [source], output, default);
                var pixels = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", h264, "-frames:v", "1",
                    "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
                Assert.Equal(64 * 36 * 3, pixels.Length);
                var left = (18 * 64 + 8) * 3;
                var right = (18 * 64 + 48) * 3;
                Assert.InRange(pixels[left], scale == 1 ? 0 : 230, scale == 1 ? 15 : 255);
                Assert.InRange(pixels[right], 230, 255);
                Assert.InRange(pixels[right + 2], 0, 15);
            }
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task FractionalSampleBoundaryAndFade_ProduceExactContiguousPcm()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-export-fade-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=1",
                "-f", "lavfi", "-i", "aevalsrc=0.1:s=48000:d=1", "-c:v", "libx264", "-c:a", "pcm_f32le", path]);
            // Audio rendering uses source seconds, not video packet ticks. Two intervals share
            // one cumulative sample grid: 1/90000 sec owns sample 0; remainder owns 47999 samples.
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "fractional", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                    FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_f32le",
                    Timing = new(new(1, 90000), 0, 90000) }], Clips = [
                    new() { Id = Guid.NewGuid(), SourceId = id, Name = "pre", InPts = 0, OutPts = 1 },
                    new() { Id = Guid.NewGuid(), SourceId = id, Name = "fade", InPts = 1, OutPts = 90000,
                        FadeInTicks = TimeSpan.TicksPerSecond / 10, FadeOutTicks = TimeSpan.TicksPerSecond / 10 }] };
            var plan = ProjectRenderPlan.Create(project);
            await using var source = File.OpenRead(path);
            var outputPath = Path.Combine(root.FullName, "samples.pcm");
            await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite))
                await new MacProjectMediaProcess(ffmpeg).RunAsync(ProjectMediaJob.RenderClipAudio(plan, 1), [source], output, default);
            var samples = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(47999 * 8, samples.Length);
            Assert.InRange(Math.Abs(BitConverter.ToSingle(samples, 0)), 0, 0.001);
            Assert.InRange(Math.Abs(BitConverter.ToSingle(samples, 24000 * 8)), 0.06, 0.11);
            Assert.InRange(Math.Abs(BitConverter.ToSingle(samples, samples.Length - 8)), 0, 0.001);
        }
        finally { root.Delete(true); }
    }
}
