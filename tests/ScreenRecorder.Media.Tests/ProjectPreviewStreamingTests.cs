// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPreviewStreamingTests
{
    [MacOsOnlyFact]
    public async Task PreviewUsesExportEffectsAndExactSeekSampleBoundary()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preview-render-");
        try
        {
            var path = Path.Combine(root.FullName, "source.mkv");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=1",
                "-f", "lavfi", "-i", "aevalsrc=0.1:s=48000:d=1", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le", path]);
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "preview", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                    FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_f32le",
                    Timing = new(new(1, 1000), 0, 1000) }], Clips = [new() { Id = Guid.NewGuid(), SourceId = id,
                    Name = "effect", InPts = 0, OutPts = 1000, PositionX = 50, Volume = .5, FadeInTicks = 5000000 }] };
            var plan = ProjectRenderPlan.Create(project);
            await using var source = File.OpenRead(path);
            using var video = new MemoryStream();
            await new MacProjectMediaProcess(ffmpeg).RunStreamingAsync(ProjectMediaJob.PreviewVideo(plan, 0, 15, 64, 36),
                source, async (s, ct) => await s.CopyToAsync(video, ct), default);
            Assert.Equal(15 * 64 * 36 * 4, video.Length);
            var pixels = video.ToArray();
            Assert.InRange(pixels[(18 * 64 + 8) * 4], 0, 15);
            Assert.InRange(pixels[(18 * 64 + 48) * 4], 230, 255);
            using var audio = new MemoryStream();
            await new MacProjectMediaProcess(ffmpeg).RunStreamingAsync(ProjectMediaJob.PreviewAudio(plan, 0, 24000),
                source, async (s, ct) => await s.CopyToAsync(audio, ct), default);
            Assert.Equal(24000 * 8, audio.Length);
            // Seeking after the fade must not restart it; mono-to-stereo gain is ~sqrt(1/2).
            Assert.InRange(BitConverter.ToSingle(audio.ToArray(), 0), .03, .051);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public async Task BoundStreamingConsumesEveryFrameWithoutWholeOutputBuffer()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-product-stream-");
        try
        {
            var path = Path.Combine(root.FullName, "frame");
            var expected = Enumerable.Range(0, 160 * 90 * 4).Select(i => (byte)(i % 251)).ToArray();
            await File.WriteAllBytesAsync(path, expected);
            await using var source = File.OpenRead(path);
            var helper = Path.Combine(root.FullName, "reader.sh");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nexec /bin/cat\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var output = new MemoryStream();
            await new MacProjectMediaProcess(helper).RunStreamingAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 160, 90), source,
                async (stream, ct) => await stream.CopyToAsync(output, ct), default);
            Assert.Equal(expected, output.ToArray());
            Assert.True(source.CanRead);
        }
        finally { root.Delete(true); }
    }
}
