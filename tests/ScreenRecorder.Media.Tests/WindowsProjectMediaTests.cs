// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsProjectMediaTests
{
    [NativeMediaTheory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task TwoBoundInputsProduceVerifiedMp4WithoutReopeningOutputPath(int segmentCount)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-win-mux-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var videoPath = Path.Combine(root.FullName, "video.h264");
            var audioPath = Path.Combine(root.FullName, "audio.pcm");
            var outputPath = Path.Combine(root.FullName, "result.mp4");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i",
                "color=blue:s=64x36:r=30:d=1", "-c:v", "libx264", "-bf", "0", "-f", "h264", videoPath]);
            var segment = await File.ReadAllBytesAsync(videoPath);
            await using (var video = new FileStream(videoPath, FileMode.Append, FileAccess.Write))
                for (var i = 1; i < segmentCount; i++) await video.WriteAsync(segment);
            await File.WriteAllBytesAsync(audioPath, new byte[segmentCount * 48000 * 8]);
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "Mux", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s",
                    RelativePath = "sources/a.mkv", FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36,
                    VideoCodec = "h264", AudioCodec = "aac", Timing = new(new(1, 1000), 0, segmentCount * 1000) }],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "clip", InPts = 0, OutPts = segmentCount * 1000 }] };
            var plan = ProjectRenderPlan.Create(project);
            IProjectMediaProcess process = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            await using (var video = File.OpenRead(videoPath))
            await using (var audio = File.OpenRead(audioPath))
            await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                await process.RunAsync(ProjectMediaJob.MuxEditedMp4(plan, 16 * 1024 * 1024), [video, audio], output, default);
                Assert.True(output.Length > 0);
            }
            await using var verified = File.OpenRead(outputPath);
            await ProjectOutputVerifier.VerifyAsync(process, verified, plan, default);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task WritableSourceIsRejectedBeforeStartingNativeChild()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var source = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            var process = new WindowsProjectMediaProcess(Path.GetFullPath("ffmpeg.exe"));
            await Assert.ThrowsAsync<ArgumentException>(() => process.RunAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 64, 36), [source], null, default));
            Assert.True(source.CanWrite);
        }
        finally { File.Delete(path); }
    }

    [WindowsOnlyFact]
    public async Task BoundHandleDecodesAfterSourceRenameAndMatchesStreaming()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-win-project-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i",
                "color=red:s=64x36:r=30:d=1", "-c:v", "libx264", "-an", path]);
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            File.Move(path, path + ".original");
            await File.WriteAllTextAsync(path, "replacement must never be opened");
            var process = new WindowsProjectMediaProcess(ffmpeg);
            var job = ProjectMediaJob.ExtractFrame(500, new(1, 1000), 64, 36);
            var result = await process.RunAsync(job, [source], null, default);
            Assert.Equal(64 * 36 * 4, result.Output.Length);
            Assert.InRange(result.Output[0], 230, 255);
            Assert.InRange(result.Output[2], 0, 20);
            using var streamed = new MemoryStream();
            await process.RunStreamingAsync(job, source, (s, ct) => s.CopyToAsync(streamed, ct), default);
            Assert.Equal(result.Output, streamed.ToArray());
            Assert.Equal("replacement must never be opened", await File.ReadAllTextAsync(path));
        }
        finally { root.Delete(true); }
    }
}
