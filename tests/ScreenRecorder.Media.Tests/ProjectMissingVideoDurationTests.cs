// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMissingVideoDurationTests
{
    private const string Video = "stream|index=0|codec_type=video|codec_name=h264|width=64|height=36|pix_fmt=yuv420p|time_base=1/1000|r_frame_rate=30/1|extradata_hash=SHA256:abc\n";

    [Theory]
    [InlineData("")]
    [InlineData("|duration=N/A")]
    public async Task OrderedVideoUsesFollowingPtsAndChecksLastIntervalAgainstFrameRate(string duration)
    {
        var observed = new List<(long Pts, long Duration)>();
        var text = string.Concat(new[] { 0, 33, 67 }.Select(p => $"packet|stream_index=0|pts={p}|dts={p}{duration}|flags=K_\n"));
        var evidence = await ProjectPacketInspector.ParseAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text + Video)),
            default, (_, pts, length) => observed.Add((pts, length)));
        Assert.Equal(3, evidence.Video.PacketCount);
        Assert.Equal(101, evidence.Video.EndPts);
        Assert.Equal(new[] { (0L, 33L), (33L, 34L), (67L, 34L) }, observed);
        Assert.True(evidence.Video.MonotonicDts);
        var explicitDurations = text.Replace("pts=0|dts=0" + duration, "pts=0|dts=0|duration=33")
            .Replace("pts=33|dts=33" + duration, "pts=33|dts=33|duration=34")
            .Replace("pts=67|dts=67" + duration, "pts=67|dts=67|duration=34");
        var original = await ProjectExportStrategyTests.Parse(explicitDurations + Video);
        Assert.Equal(original.Video.PresentationTimingHash, evidence.Video.PresentationTimingHash);
    }

    [Theory]
    [InlineData("audio", "aac")]
    [InlineData("video", "hevc")]
    public async Task MissingDurationsAreNotInferredForAudioOrOtherCodecs(string type, string codec)
    {
        var packets = string.Concat(new[] { 0, 33, 67 }.Select(p => $"packet|stream_index=1|pts={p}|dts={p}|duration=N/A|flags=K_\n"));
        var metadata = Video.Replace("index=0", "index=1").Replace("codec_type=video", "codec_type=" + type).Replace("codec_name=h264", "codec_name=" + codec);
        var validVideo = "packet|stream_index=0|pts=0|dts=0|duration=33|flags=K_\n" + Video;
        await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() => ProjectExportStrategyTests.Parse(
            type == "audio" ? validVideo + packets + metadata : packets + metadata));
    }

    [Theory]
    [InlineData("0,67,33", "30/1")]
    [InlineData("0,33,33", "30/1")]
    [InlineData("0,33,200", "30/1")]
    [InlineData("0,33,67", "0/0")]
    [InlineData("0", "30/1")]
    public async Task AmbiguousMissingDurationStillRequiresRendering(string pts, string rate)
    {
        var packets = string.Concat(pts.Split(',').Select(p => $"packet|stream_index=0|pts={p}|dts={p}|duration=N/A|flags=K_\n"));
        await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() => ProjectExportStrategyTests.Parse(packets + Video.Replace("30/1", rate)));
    }

    [NativeMediaTheory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public async Task MissingVideoProbeDurationPreservesActualSingleAndJoinedVideo(int count, bool audio)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-missing-video-duration-");
        try
        {
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "Fast recording");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var sources = new List<ProjectSource>(); var payloads = new List<byte[]>();
            for (var i = 0; i < Math.Min(count, 2); i++)
            {
                var name = $"sources/{i}.mkv"; var path = Path.Combine(owner.ProjectDirectory, name);
                await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", $"color={(i == 0 ? "red" : "blue")}:s=64x36:r=30:d=1",
                    ..(audio ? new[] { "-f", "lavfi", "-i", $"sine=frequency={440 + i * 440}:sample_rate=48000:duration=1", "-ac", "2", "-c:a", "aac" } : new[] { "-an" }),
                    "-c:v", "libx264", "-bf", "0", path]);
                var bytes = await File.ReadAllBytesAsync(path);
                await using var input = File.OpenRead(path);
                var info = await new ProjectSourceProbe().ProbeAsync(input);
                sources.Add(new() { Id = Guid.NewGuid(), SessionId = "fixture", RelativePath = name,
                    Width = info.Width, Height = info.Height, VideoCodec = info.VideoCodec, AudioCodec = info.AudioCodec,
                    Timing = info.Timing, FileSize = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                payloads.Add(await Encoded(path));
            }
            var project = owner.Current with { Revision = 1, Canvas = new(64, 36, new(30, 1)), Sessions = ["fixture"], Sources = [..sources],
                Clips = [..Enumerable.Range(0, count).Select(i => new ProjectClip { Id = Guid.NewGuid(), SourceId = sources[i % sources.Count].Id,
                    Name = $"clip {i}", InPts = sources[i % sources.Count].Timing.StartPts,
                    OutPts = sources[i % sources.Count].Timing.StartPts + sources[i % sources.Count].Timing.DurationTs })] };
            await owner.SaveAsync(project, 0);
            var phases = new Phases();
            IProjectMediaProcess process = OperatingSystem.IsWindows() ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            var missing = new MissingVideoDurationProcess(process, count);
            var result = await new ProjectFfmpegExporter(missing).ExportAsync(owner, project,
                Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName, Guid.NewGuid(), phases, default);
            Assert.True(result.Success, result.Error);
            Assert.True(missing.RemovedDurations >= count * 30);
            Assert.DoesNotContain(RecordingExportPhase.Rendering, phases.Values);
            Assert.Contains(count > 1 && audio ? RecordingExportPhase.ConvertingAudio : RecordingExportPhase.Copying, phases.Values);
            Assert.Equal(Enumerable.Range(0, count).SelectMany(i => payloads[i % payloads.Count]).ToArray(), await Encoded(result.FinalPath!));
            var pixels = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", result.FinalPath!, "-an", "-pix_fmt", "rgb24",
                "-c:v", "rawvideo", "-threads", "1", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(count * 30 * 64 * 36 * 3, pixels.Length);
            if (audio)
            {
                var samples = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", result.FinalPath!, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                Assert.InRange(samples.Length / 4, count * 47900, count * 48100 + 1024);
            }
            foreach (var source in sources)
                Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(owner.ProjectDirectory, source.RelativePath)))));

            Task<byte[]> Encoded(string path) => RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", path, "-map", "0:v:0", "-c:v", "copy", "-an",
                "-bsf:v", "h264_mp4toannexb", "-f", "h264", "pipe:1"]);
        }
        finally { root.Delete(true); }
    }

    // Only the optional source video duration probe field is removed. Packet data,
    // PTS/DTS, audio evidence, native export and output verification are all real.
    private sealed class MissingVideoDurationProcess(IProjectMediaProcess inner, int sourceProbes) : IProjectMediaProcess, IProjectStreamingMediaProcess
    {
        private int _probes;
        public int RemovedDurations { get; private set; }
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs, FileStream? output, CancellationToken ct)
            => inner.RunAsync(job, inputs, output, ct);
        public Task RunStreamingAsync(ProjectMediaJob job, FileStream input, Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
            => ((IProjectStreamingMediaProcess)inner).RunStreamingAsync(job, input, async (stream, token) =>
            {
                if (++_probes > sourceProbes) { await consume(stream, token); return; }
                using var reader = new StreamReader(stream, leaveOpen: true);
                var text = await reader.ReadToEndAsync(token);
                var lines = text.Split('\n').Select(line =>
                {
                    if (!line.StartsWith("packet|", StringComparison.Ordinal) || !line.Split('|').Contains("stream_index=0")) return line;
                    return System.Text.RegularExpressions.Regex.Replace(line, @"\|duration=[^|\r\n]*", _ =>
                    { RemovedDurations++; return "|duration=N/A"; });
                });
                using var changed = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)));
                await consume(changed, token);
            }, ct);
    }

    private sealed class Phases : IRecordingExportProgress
    {
        public List<RecordingExportPhase> Values { get; } = [];
        public void Report(double value) { }
        public void ReportPhase(RecordingExportPhase phase) => Values.Add(phase);
    }
}
