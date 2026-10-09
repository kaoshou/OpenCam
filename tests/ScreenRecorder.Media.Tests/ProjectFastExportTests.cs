using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class MacOsOnlyTheoryAttribute : TheoryAttribute
{
    public MacOsOnlyTheoryAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "Requires native macOS media backend."; }
}

public class ProjectFastExportTests
{
    [MacOsOnlyTheory]
    [InlineData("none")][InlineData("aac")][InlineData("pcm_s16le")]
    public async Task WholeRecordingRetainsVideoAndExpectedSound(string audio)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-fast-test-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var input = Path.Combine(root.FullName, "input.mkv");
            var args = new List<string> { "-f", "lavfi", "-i", "testsrc2=s=64x36:r=30:d=1" };
            if (audio != "none") args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-c:a", audio]);
            args.AddRange(["-vf", "setpts=PTS+2/TB", "-c:v", "libx264", "-bf", "0"]);
            if (audio != "none") args.AddRange(["-af", "asetpts=PTS+2/TB"]);
            args.Add(input);
            await RecordingContentExportIntegrationTests.Run(ffmpeg, args.ToArray());
            var process = new MacProjectMediaProcess(ffmpeg);
            await using var source = File.OpenRead(input);
            var evidence = await ProjectPacketInspector.InspectAsync(process, source, default);
            Assert.Equal(30, evidence.Video.PacketCount);
            Assert.Equal("h264", evidence.Video.Codec);
            Assert.NotEmpty(evidence.Video.InitializationHash);
            var project = ProjectExportStrategyTests.Project();
            var v = evidence.Video;
            project = project with { Sources = [project.Sources[0] with { FileSize = source.Length,
                Timing = new(v.TimeBase, v.FirstPts, v.EndPts-v.FirstPts), AudioCodec = evidence.Audio?.Codec }],
                Clips = [project.Clips[0] with { InPts = v.FirstPts, OutPts = v.EndPts }] };
            var job = ProjectMediaJob.RemuxWholeRecording(ProjectRenderPlan.Create(project), audio == "pcm_s16le", audio != "none");
            var path = Path.Combine(root.FullName, "out.mp4");
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
                await process.RunAsync(job, [source], output, default);
            await using (var output = File.OpenRead(path))
            {
                await ProjectRemuxVerifier.VerifyAsync(process, process, output, [evidence], audio == "pcm_s16le", default);
                foreach (var wrong in new[] {
                    evidence with { Video = evidence.Video with { PacketCount = evidence.Video.PacketCount + 1 } },
                    evidence with { Video = evidence.Video with { Width = 128 } },
                    evidence with { Video = evidence.Video with { EndPts = evidence.Video.EndPts + 1000 } },
                    evidence with { Audio = evidence.Audio is null ? ProjectExportStrategyTests.Evidence().Audio : null } })
                    await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() => ProjectRemuxVerifier.VerifyAsync(
                        process, process, output, [wrong], audio == "pcm_s16le", default));
            }
            var original = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-i", input, "-map", "0:v", "-an", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
            var exported = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-i", path, "-map", "0:v", "-an", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(original, exported);
            if (audio != "none")
            {
                var pcm = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", path, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                Assert.True(pcm.Length >= 48000 * 4);
                Assert.Contains(Enumerable.Range(4000, 4000), i => Math.Abs(BitConverter.ToSingle(pcm, i*4)) > .01);
            }
        }
        finally { root.Delete(true); }
    }
}
