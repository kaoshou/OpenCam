using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class NativeMediaTheoryAttribute : TheoryAttribute
{
    public NativeMediaTheoryAttribute() { if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Skip = "Native media backend required."; }
}

public class ProjectConcatExportTests
{
    [Theory]
    [InlineData(47, 0)] // AAC packet padding differs from the 960 ms video interval.
    [InlineData(45, -21)] // Exact packet count still cannot carry per-segment priming.
    public void ConcatRejectsAacThatNeedsNormalization(long packetCount, long firstPts)
    {
        var evidence = ProjectExportStrategyTests.Evidence();
        evidence = evidence with { Video = evidence.Video with { EndPts = 960 },
            Audio = evidence.Audio! with { PacketCount = packetCount, FirstPts = firstPts } };
        Assert.Throws<ProjectRemuxIncompatibleException>(() =>
            ProjectMediaJob.ConcatWholeRecordings([evidence, evidence], 2000));
        // Copying only the video remains safe even when the omitted audio needs rendering.
        Assert.NotNull(ProjectMediaJob.ConcatWholeRecordings([evidence, evidence], 2000, videoOnly: true));
    }

    [Fact]
    public void ConcatAllowsSampleAlignedAacWithoutPerSegmentPadding()
    {
        var evidence = ProjectExportStrategyTests.Evidence();
        evidence = evidence with { Video = evidence.Video with { EndPts = 960 },
            Audio = evidence.Audio! with { PacketCount = 45, FirstPts = 0, EndPts = 960 } };
        Assert.NotNull(ProjectMediaJob.ConcatWholeRecordings([evidence, evidence], 2000));
    }

    [NativeMediaTheory]
    [InlineData(2, false)][InlineData(10, false)][InlineData(100, false)]
    [InlineData(2, true)][InlineData(10, true)][InlineData(100, true)]
    public async Task CompleteSegmentsRetainOrderThroughSupportedExportRoutes(int count, bool audio)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-concat-");
        var inputs = new List<FileStream>();
        try
        {
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "Concat acceptance");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess process = OperatingSystem.IsWindows() ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            var streaming = (IProjectStreamingMediaProcess)process;
            foreach (var color in new[] { "red", "blue" })
            {
                var args = new List<string> { "-f", "lavfi", "-i", $"color={color}:s=64x36:r=30:d=1" };
                if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=frequency={(color == "red" ? 440 : 880)}:sample_rate=48000:duration=1", "-c:a", "aac"]);
                args.AddRange(["-c:v", "libx264", "-bf", "0", Path.Combine(owner.ProjectDirectory, "sources", color + " '特殊.mkv")]);
                await RecordingContentExportIntegrationTests.Run(ffmpeg, args.ToArray());
            }
            var evidence = new List<ProjectSourceEvidence>();
            for (var i = 0; i < count; i++)
            {
                var input = File.OpenRead(Path.Combine(owner.ProjectDirectory, "sources", (i % 2 == 0 ? "blue" : "red") + " '特殊.mkv"));
                inputs.Add(input);
                evidence.Add(await ProjectPacketInspector.InspectAsync(streaming, input, default));
            }
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            string path;
            ProjectSource[] sources = [];
            if (audio)
            {
                // Per-segment AAC padding is not copy-safe, even when a short join sounds right.
                Assert.Throws<ProjectRemuxIncompatibleException>(() =>
                    ProjectMediaJob.ConcatWholeRecordings(evidence, inputs.Sum(f => f.Length)));
                sources = Enumerable.Range(0, 2).Select(i => new ProjectSource {
                    Id = Guid.NewGuid(), SessionId = "fixture", RelativePath = "sources/" + Path.GetFileName(inputs[i].Name),
                    Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "aac", FileSize = inputs[i].Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inputs[i].Name))),
                    Timing = new(evidence[i].Video.TimeBase, evidence[i].Video.FirstPts,
                        evidence[i].Video.EndPts - evidence[i].Video.FirstPts) }).ToArray();
                var project = owner.Current with { Revision = 1, Canvas = new(64, 36, new(30, 1)),
                    Sessions = ["fixture"], Sources = [..sources],
                    Clips = [..Enumerable.Range(0, count).Select(i => new ProjectClip {
                        Id = Guid.NewGuid(), Name = "segment " + i, SourceId = sources[i % 2].Id,
                        InPts = sources[i % 2].Timing.StartPts,
                        OutPts = sources[i % 2].Timing.StartPts + sources[i % 2].Timing.DurationTs })] };
                await owner.SaveAsync(project, 0);
                Assert.Equal(ProjectExportStrategy.ConcatConvertAudio,
                    ProjectExportPlanner.Select(ProjectRenderPlan.Create(project), evidence).Strategy);
                var export = await new ProjectFfmpegExporter(process).ExportAsync(owner, project, destination,
                    Guid.NewGuid(), new Progress<double>(), default);
                Assert.True(export.Success, export.Error);
                path = Assert.IsType<string>(export.FinalPath);
            }
            else
            {
                // Complete silent segments still exercise the exact packet-copy fast path.
                var manifestPath = Path.Combine(root.FullName, "concat.txt");
                await File.WriteAllTextAsync(manifestPath, ProjectMediaJob.CreateConcatManifest(evidence));
                await using var manifest = File.OpenRead(manifestPath);
                path = Path.Combine(destination, "out.mp4");
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
                await process.RunAsync(ProjectMediaJob.ConcatWholeRecordings(evidence, inputs.Sum(f => f.Length)), [manifest, ..inputs], output, default);
            }
            await using var result = File.OpenRead(path);
            var actual = await ProjectPacketInspector.InspectAsync(streaming, result, default);
            Assert.Equal(count * 30, actual.Video.PacketCount);
            Assert.InRange(actual.Video.EndSeconds - actual.Video.StartSeconds, count - .002, count + .002);
            var decoded = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-i", path, "-map", "0:v", "-an", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(count * 30 * 64 * 36 * 3, decoded.Length);
            for (var i = 0; i < count; i++) Assert.True(decoded[i * 30 * 64 * 36 * 3 + (i % 2 == 0 ? 2 : 0)] > 200);
            if (audio)
            {
                var pcm = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", path, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                Assert.InRange(pcm.Length / 4, count * 48000, count * 48000 + 1024); // Only terminal AAC padding.
                for (var i = 0; i < count; i++)
                {
                    var start = i * 48000 + 12000;
                    var crossings = Enumerable.Range(start, 12000).Count(n => BitConverter.ToSingle(pcm, n*4) <= 0 && BitConverter.ToSingle(pcm, (n+1)*4) > 0);
                    Assert.InRange(crossings, (i % 2 == 0 ? 220 : 110) - 2, (i % 2 == 0 ? 220 : 110) + 2);
                }
                foreach (var source in sources)
                    Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                        Path.Combine(owner.ProjectDirectory, source.RelativePath)))));
                Assert.Single(Directory.GetFiles(destination)); // Verified publication removes owned intermediates.
            }
            else await ProjectRemuxVerifier.VerifyAsync(process, streaming, result, evidence, false, default);
        }
        finally { foreach (var input in inputs) await input.DisposeAsync(); root.Delete(true); }
    }
}
