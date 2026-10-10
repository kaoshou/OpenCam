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
    [InlineData(2, true)][InlineData(3, true)][InlineData(10, true)][InlineData(100, true)]
    [InlineData(3, true, 2)]
    [InlineData(3, true, 0, .1, .873)]
    [InlineData(2, true, 0, 0, 1, true)]
    [InlineData(2, true, 0, 0, 1, false, 1)]
    [InlineData(2, true, 0, 0, 1, false, 2)]
    [InlineData(3, true, 0, 0, 1, false, 0, true)]
    public async Task CompleteSegmentsRetainOrderThroughSupportedExportRoutes(int count, bool audio,
        int bFrames = 0, double audioDelay = 0, double audioDuration = 1, bool cancel = false, int fault = 0,
        bool variable = false)
    {
        var framesPerClip = variable ? 23 : 30;
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
                if (audio) args.AddRange(["-f", "lavfi", "-i", FormattableString.Invariant($"aevalsrc=0.08*sin(2*PI*{(color == "red" ? 440 : 880)}*t)+0.7*eq(n\\,2400)+0.7*eq(n\\,43200):s=48000:d={audioDuration}"), "-c:a", "aac"]);
                if (audioDelay > 0) args.AddRange(["-af", FormattableString.Invariant($"asetpts=PTS+{audioDelay}/TB")]);
                if (variable) args.AddRange(["-vf", "select=lt(n\\,15)+not(mod(n\\,2))+eq(n\\,29)", "-fps_mode", "vfr"]);
                args.AddRange(["-c:v", "libx264", "-bf", bFrames.ToString(), Path.Combine(owner.ProjectDirectory, "sources", color + " '特殊.mkv")]);
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
                using var cancellation = new CancellationTokenSource();
                var progress = new PhaseProgress(value => {
                    if (cancel && value > .2 && value < .9) cancellation.Cancel();
                });
                var exporter = new ProjectFfmpegExporter(fault == 0 ? process : new FailingFinalProbe(process, fault, count));
                if (cancel || fault == 2)
                {
                    Task<RecordingExportResult> Run() => exporter.ExportAsync(
                        owner, project, destination, Guid.NewGuid(), progress, cancellation.Token);
                    if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
                    else await Assert.ThrowsAsync<IOException>(Run);
                    Assert.Empty(Directory.GetFiles(destination));
                    Assert.DoesNotContain(RecordingExportPhase.Rendering, progress.Phases);
                    foreach (var original in sources)
                        Assert.Equal(original.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                            Path.Combine(owner.ProjectDirectory, original.RelativePath)))));
                    return;
                }
                var export = await exporter.ExportAsync(owner, project, destination,
                    Guid.NewGuid(), progress, cancellation.Token);
                Assert.True(export.Success, export.Error);
                Assert.Equal(fault == 1
                    ? new[] { RecordingExportPhase.Inspecting, RecordingExportPhase.ConvertingAudio,
                        RecordingExportPhase.Verifying, RecordingExportPhase.Rendering, RecordingExportPhase.Verifying }
                    : new[] { RecordingExportPhase.Inspecting, RecordingExportPhase.ConvertingAudio,
                        RecordingExportPhase.Verifying }, progress.Phases);
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
            Assert.Equal(count * framesPerClip, actual.Video.PacketCount);
            Assert.InRange(actual.Video.EndSeconds - actual.Video.StartSeconds, count - .002, count + .002);
            var decoded = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-i", path, "-map", "0:v", "-an", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(count * framesPerClip * 64 * 36 * 3, decoded.Length);
            for (var i = 0; i < count; i++) Assert.True(decoded[i * framesPerClip * 64 * 36 * 3 + (i % 2 == 0 ? 2 : 0)] > 200);
            if (audio)
            {
                // Stream-copy must preserve encoded packets, not merely similar decoded colours.
                var packetHashes = new List<string>();
                for (var i = 0; i < 2; i++)
                    packetHashes.AddRange(await VideoPacketHashes(inputs[i].Name));
                var expectedHashes = Enumerable.Range(0, count).SelectMany(i => packetHashes.Skip(i % 2 * framesPerClip).Take(framesPerClip));
                if (fault == 0) Assert.Equal(expectedHashes, await VideoPacketHashes(path));
                if (fault == 0)
                {
                    var outputPackets = await VideoTiming(path);
                    double offset = 0;
                    for (var i = 0; i < count; i++)
                    {
                        var sourcePackets = await VideoTiming(inputs[i].Name);
                        var origin = sourcePackets.Min(p => p.Pts);
                        for (var j = 0; j < sourcePackets.Length; j++)
                        {
                            var packet = outputPackets[i * framesPerClip + j];
                            Assert.InRange(Math.Abs(packet.Pts - (sourcePackets[j].Pts - origin + offset)), 0, .0021);
                            Assert.InRange(Math.Abs(packet.Dts - (sourcePackets[j].Dts - origin + offset)), 0, .0021);
                        }
                        offset += evidence[i].Video.EndSeconds - evidence[i].Video.StartSeconds;
                    }
                }
                var changedPackets = evidence.Select(e => e with { Video = e.Video with { PacketHash = new string('0', 64) } }).ToArray();
                await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() => ProjectRemuxVerifier.VerifyAsync(
                    process, streaming, result, changedPackets, true, default, normalizedAudioSamples: count * 48000));
                if (fault == 0)
                {
                    var changedTiming = evidence.Select(e => e with { Video = e.Video with { PresentationTimingHash = new string('0', 64) } }).ToArray();
                    await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() => ProjectRemuxVerifier.VerifyAsync(
                        process, streaming, result, changedTiming, true, default, normalizedAudioSamples: count * 48000));
                }
                var pcm = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", path, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                Assert.InRange(pcm.Length / 4, count * 48000, count * 48000 + 1024); // Only terminal AAC padding.
                for (var i = 0; i < count; i++)
                {
                    var start = i * 48000 + 12000;
                    var crossings = Enumerable.Range(start, 12000).Count(n => BitConverter.ToSingle(pcm, n*4) <= 0 && BitConverter.ToSingle(pcm, (n+1)*4) > 0);
                    Assert.InRange(crossings, (i % 2 == 0 ? 220 : 110) - 2, (i % 2 == 0 ? 220 : 110) + 2);
                    // Locate impulses on both sides of every join, not merely total length.
                    foreach (var marker in new[] { 2400, 43200 }.Where(n => n < audioDuration * 48000))
                    {
                        var expectedPosition = i * 48000 + marker + (int)Math.Round(audioDelay * 48000);
                        var peak = Enumerable.Range(expectedPosition - 480, 961)
                            .MaxBy(n => Math.Abs(BitConverter.ToSingle(pcm, n * 4)));
                        Assert.InRange(Math.Abs(peak - expectedPosition), 0, 96);
                    }
                    if (audioDelay > 0)
                    {
                        // Hand-placed onset: each new clip must retain its leading silence,
                        // not slide the next clip's speech back to the boundary.
                        var quiet = Enumerable.Range(i * 48000 + 1000, 1000).Average(n => Math.Abs(BitConverter.ToSingle(pcm, n * 4)));
                        var audible = Enumerable.Range(i * 48000 + 10000, 1000).Average(n => Math.Abs(BitConverter.ToSingle(pcm, n * 4)));
                        Assert.True(quiet < .005, $"clip {i}: leading audio moved ({quiet})");
                        Assert.True(audible > .02);
                    }
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

    private static async Task<string[]> VideoPacketHashes(string path)
    {
        var bytes = await RecordingContentExportIntegrationTests.Run(FFmpegDiscovery.FindFFmpegExecutable()!,
            ["-i", path, "-map", "0:v:0", "-c:v", "copy", "-f", "framehash", "-hash", "sha256", "pipe:1"]);
        return System.Text.Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim()).ToArray();
    }

    private static async Task<(double Pts, double Dts)[]> VideoTiming(string path)
    {
        var bytes = await RecordingContentExportIntegrationTests.Run(FFmpegDiscovery.FindFFmpegExecutable()!,
            ["-i", path, "-map", "0:v:0", "-c:v", "copy", "-f", "framehash", "pipe:1"]);
        var lines = System.Text.Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ratio = lines.Single(l => l.StartsWith("#tb 0:")).Split(':')[1].Trim().Split('/');
        var tick = double.Parse(ratio[0], System.Globalization.CultureInfo.InvariantCulture) / double.Parse(ratio[1], System.Globalization.CultureInfo.InvariantCulture);
        return lines.Where(l => !l.StartsWith('#')).Select(l => l.Split(','))
            .Select(p => (long.Parse(p[2]) * tick, long.Parse(p[1]) * tick)).ToArray();
    }

    private sealed class PhaseProgress(Action<double> changed) : IRecordingExportProgress
    {
        public List<RecordingExportPhase> Phases { get; } = [];
        public void Report(double value) => changed(value);
        public void ReportPhase(RecordingExportPhase phase) => Phases.Add(phase);
    }

    // Fail only the final copy-output inspection, keeping real source reads, PCM,
    // rendering and file publication. I/O failure must not be mistaken for incompatibility.
    private sealed class FailingFinalProbe(IProjectMediaProcess inner, int fault, int sourceCount)
        : IProjectMediaProcess, IProjectStreamingMediaProcess
    {
        private int _probes;
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct) => inner.RunAsync(job, inputs, output, ct);
        public Task RunStreamingAsync(ProjectMediaJob job, FileStream input,
            Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
        {
            // Bound-directory streams are handle-only and deliberately have no path name.
            if (++_probes > sourceCount)
                throw fault == 1 ? new ProjectRemuxIncompatibleException("Injected output boundary mismatch")
                    : new IOException("Injected read failure");
            return ((IProjectStreamingMediaProcess)inner).RunStreamingAsync(job, input, consume, ct);
        }
    }
}
