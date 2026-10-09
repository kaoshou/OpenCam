using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public sealed class NativeMediaTheoryAttribute : TheoryAttribute
{
    public NativeMediaTheoryAttribute() { if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Skip = "Native media backend required."; }
}

public class ProjectConcatExportTests
{
    [NativeMediaTheory]
    [InlineData(2, false)][InlineData(10, false)][InlineData(100, false)]
    [InlineData(2, true)][InlineData(10, true)][InlineData(100, true)]
    public async Task CompleteSegmentsRetainOrderWithoutEncoding(int count, bool audio)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-concat-");
        var inputs = new List<FileStream>();
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess process = OperatingSystem.IsWindows() ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            var streaming = (IProjectStreamingMediaProcess)process;
            foreach (var color in new[] { "red", "blue" })
            {
                var args = new List<string> { "-f", "lavfi", "-i", $"color={color}:s=64x36:r=30:d=1" };
                if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=frequency={(color == "red" ? 440 : 880)}:sample_rate=48000:duration=1", "-c:a", "aac"]);
                args.AddRange(["-c:v", "libx264", "-bf", "0", Path.Combine(root.FullName, color + " '特殊.mkv")]);
                await RecordingContentExportIntegrationTests.Run(ffmpeg, args.ToArray());
            }
            var evidence = new List<ProjectSourceEvidence>();
            for (var i = 0; i < count; i++)
            {
                var input = File.OpenRead(Path.Combine(root.FullName, (i % 2 == 0 ? "blue" : "red") + " '特殊.mkv"));
                inputs.Add(input);
                evidence.Add(await ProjectPacketInspector.InspectAsync(streaming, input, default));
            }
            var manifestPath = Path.Combine(root.FullName, "concat.txt");
            await File.WriteAllTextAsync(manifestPath, ProjectMediaJob.CreateConcatManifest(evidence));
            await using var manifest = File.OpenRead(manifestPath);
            var path = Path.Combine(root.FullName, "out.mp4");
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
                await process.RunAsync(ProjectMediaJob.ConcatWholeRecordings(evidence, inputs.Sum(f => f.Length)), [manifest, ..inputs], output, default);
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
                for (var i = 0; i < count; i++)
                {
                    var start = i * 48000 + 12000;
                    var crossings = Enumerable.Range(start, 12000).Count(n => BitConverter.ToSingle(pcm, n*4) <= 0 && BitConverter.ToSingle(pcm, (n+1)*4) > 0);
                    Assert.InRange(crossings, (i % 2 == 0 ? 220 : 110) - 2, (i % 2 == 0 ? 220 : 110) + 2);
                }
            }
            await ProjectRemuxVerifier.VerifyAsync(process, streaming, result, evidence, false, default);
        }
        finally { foreach (var input in inputs) await input.DisposeAsync(); root.Delete(true); }
    }
}
