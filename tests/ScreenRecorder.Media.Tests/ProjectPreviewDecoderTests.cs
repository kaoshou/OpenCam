// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPreviewDecoderTests
{
    [NativeProjectAudioFact]
    public async Task EarlyCancelJoinsDeviceAndDecoderCanRestartAtAnotherPosition()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preview-stop-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=blue:s=64x36:r=60:d=2",
                "-f", "lavfi", "-i", "aevalsrc=0.001:s=48000:d=2", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le", path]);
            var id = Guid.NewGuid();
            var source = new ProjectSource { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                FileSize = new FileInfo(path).Length, Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),
                Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_f32le", Timing = new(new(1, 1000), 0, 2000) };
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "stop", Sessions = ["s"],
                Canvas = new(64, 36, new(60, 1)), Sources = [source],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "clip", InPts = 0, OutPts = 2000 }] };
            var outputs = new List<MacProjectAudioOutput>();
            var decoder = new ProjectPreviewDecoder(() => new MacProjectMediaProcess(ffmpeg), () => {
                var output = new MacProjectAudioOutput(Environment.GetEnvironmentVariable("OPENCAM_TEST_PROJECT_AUDIO_HELPER")!);
                outputs.Add(output); return output;
            });
            for (var run = 0; run < 2; run++)
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var frames = 0;
                var start = run * 10000000L;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decoder.PlayAsync(project, start,
                    (_, _) => Task.FromResult(File.OpenRead(path)), frame => {
                        Assert.True(frame.TimelineTicks >= start);
                        if (++frames == 4) cancel.Cancel();
                        return Task.CompletedTask;
                    }, _ => { }, cancel.Token));
                Assert.Equal(4, frames);
                Assert.All(outputs, output => Assert.False(output.IsRunning));
                await Task.Delay(250);
                Assert.Equal(4, frames);
            }
        }
        finally { root.Delete(true); }
    }
    [NativeProjectAudioFact]
    public async Task ContinuousAudioVideoCrossesTrimmedClipBoundaryAndStops()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-live-preview-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=1",
                "-f", "lavfi", "-i", "aevalsrc=0.001:s=48000:d=1", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le", path]);
            var id = Guid.NewGuid();
            var source = new ProjectSource { Id = id, SessionId = "s", RelativePath = "sources/x.mkv",
                FileSize = new FileInfo(path).Length, Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),
                Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_f32le", Timing = new(new(1, 1000), 0, 1000) };
            var first = new ProjectClip { Id = Guid.NewGuid(), SourceId = id, Name = "first", InPts = 0, OutPts = 500 };
            // Last frame at .966666… is < 1 ms before the audio endpoint: require the final clock packet.
            var second = first with { Id = Guid.NewGuid(), Name = "second", InPts = 500, OutPts = 967, Muted = true };
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "preview", Sessions = ["s"],
                Canvas = new(64, 36, new(30, 1)), Sources = [source], Clips = [first, second] };
            var seen = new List<ProjectPreviewFrame>();
            long position = 0;
            var decoder = new ProjectPreviewDecoder(() => new MacProjectMediaProcess(ffmpeg),
                () => new MacProjectAudioOutput(Environment.GetEnvironmentVariable("OPENCAM_TEST_PROJECT_AUDIO_HELPER")!));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await decoder.PlayAsync(project, 2000000, (_, _) => Task.FromResult(File.OpenRead(path)),
                frame => { seen.Add(frame); return Task.CompletedTask; }, ticks => position = ticks, timeout.Token);
            Assert.Contains(seen, f => f.ClipId == first.Id);
            Assert.Contains(seen, f => f.ClipId == second.Id);
            Assert.All(seen, f => { Assert.InRange(f.TimelineTicks, 2000000, 9669999); Assert.Equal(64 * 36 * 4, f.Rgba.Length); });
            Assert.Equal(9670000, position);
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant());
        }
        finally { root.Delete(true); }
    }
}
