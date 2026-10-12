using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class FocusedCompoundMediaTests
{
    [MacOsOnlyFact]
    public async Task MergeSaveReopenExportsBothIntervalsAndSound()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-compound-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var store = new JsonProjectStore();
            string manifest;
            string originalHash;
            await using (var owner = await store.CreateAsync(root.FullName, "merged"))
            {
                manifest = Path.Combine(owner.ProjectDirectory, "project.opencam");
                var path = Path.Combine(owner.ProjectDirectory, "sources", "test.mkv");
                await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "testsrc2=s=64x36:r=30:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_s16le", path]);
                originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                var sourceId = Guid.NewGuid();
                var project = owner.Current with { Canvas = new(64, 36, new(30, 1)), Sessions = ["test"],
                    Sources = [new() { Id = sourceId, SessionId = "test", RelativePath = "sources/test.mkv",
                        FileSize = new FileInfo(path).Length, Sha256 = originalHash, Width = 64, Height = 36,
                        VideoCodec = "h264", AudioCodec = "pcm_s16le", Timing = new(new(1, 1000), 0, 2000) }],
                    Clips = [new() { Id = Guid.NewGuid(), SourceId = sourceId, Name = "first", InPts = 0, OutPts = 500 },
                        new() { Id = Guid.NewGuid(), SourceId = sourceId, Name = "second", InPts = 1000, OutPts = 2000, Volume = .5 }] };
                var history = new ProjectEditHistory(project);
                history.Apply(new ProjectClipEdit.Merge(project.Clips[0].Id, false));
                await owner.SaveAsync(history.Current, 0);
            }
            await using (var reopened = await store.OpenAsync(manifest))
            {
                Assert.Equal(3, reopened.Current.SchemaVersion);
                var clip = Assert.Single(reopened.Current.Clips);
                Assert.Equal(2, clip.Parts.Length);
                var plan = ProjectRenderPlan.Create(reopened.Current);
                Assert.Equal(45, plan.FrameCount);
                Assert.Equal(.5, plan.Clips[1].Clip.Volume);
                var result = await new ProjectFfmpegExporter(new MacProjectMediaProcess(ffmpeg)).ExportAsync(
                    reopened, reopened.Current, root.FullName, Guid.NewGuid(), new Progress<double>(), default);
                Assert.True(result.Success, result.Error);
                var pcm = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", result.FinalPath!, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                Assert.True(pcm.Length >= 72000 * 4);
                Assert.Contains(Enumerable.Range(4000, 4000), i => Math.Abs(BitConverter.ToSingle(pcm, i * 4)) > .01);
                Assert.Contains(Enumerable.Range(50000, 4000), i => Math.Abs(BitConverter.ToSingle(pcm, i * 4)) > .01);
                var path = Path.Combine(reopened.ProjectDirectory, "sources", "test.mkv");
                Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
            }
        }
        finally { root.Delete(true); }
    }
}
