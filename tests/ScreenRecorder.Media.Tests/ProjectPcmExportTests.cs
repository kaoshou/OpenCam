// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;
using Xunit.Abstractions;

namespace ScreenRecorder.Media.Tests;

public class ProjectPcmExportTests(ITestOutputHelper output)
{
    [NativeMediaTheory]
    [InlineData("single")]
    [InlineData("join")]
    [InlineData("float-join")]
    [InlineData("cut")]
    [InlineData("mixed-size")]
    public async Task ExportRetainsVisibleCutsAndTimedAudioMarkers(string scenario)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-export-markers-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess process = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "marker test");
            var parts = new List<ProjectSource>();
            var clips = new List<ProjectClip>();
            var audioCodec = scenario == "float-join" ? "pcm_f32le" : "pcm_s16le";
            var count = scenario == "single" ? 1 : 2;
            for (var i = 0; i < count; i++)
            {
                var width = scenario == "mixed-size" && i == 1 ? 640 : 1280;
                var height = width * 9 / 16;
                var relative = $"sources/{i}.mkv";
                var path = Path.Combine(owner.ProjectDirectory, relative);
                // Movement exercises the encoder. The square identifies the clip on every frame.
                await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i",
                    $"testsrc2=size={width}x{height}:rate=30:duration=4",
                    "-f", "lavfi", "-i", @"aevalsrc=0.85*eq(mod(n\,48000)\,12000):s=48000:d=4",
                    "-vf", $"drawbox=x=0:y=0:w=64:h=64:color={(i == 0 ? "red" : "blue")}:t=fill",
                    "-c:v", "libx264", "-bf", "0", "-c:a", audioCodec, path]);
                await using var file = File.OpenRead(path);
                var evidence = await ProjectPacketInspector.InspectAsync((IProjectStreamingMediaProcess)process, file, default);
                var video = evidence.Video;
                file.Position = 0;
                var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "fixture", RelativePath = relative,
                    Width = width, Height = height, VideoCodec = "h264", AudioCodec = audioCodec, FileSize = file.Length,
                    Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file)),
                    Timing = new(video.TimeBase, video.FirstPts, video.EndPts - video.FirstPts) };
                parts.Add(source);
                clips.Add(new() { Id = Guid.NewGuid(), Name = $"clip {i}", SourceId = source.Id,
                    InPts = video.FirstPts + (scenario == "cut" && i == 1 ? 1000 : 0),
                    OutPts = video.EndPts - (scenario == "cut" && i == 0 ? 1000 : 0) });
            }
            var project = owner.Current with { Revision = 1, Canvas = new(1280, 720, new(30, 1)),
                Sessions = ["fixture"], Sources = [..parts], Clips = [..clips] };
            await owner.SaveAsync(project, 0);
            var phases = new PhaseRecorder();
            var watch = Stopwatch.StartNew();
            var result = await new ProjectFfmpegExporter(process).ExportAsync(owner, project, root.FullName, Guid.NewGuid(), phases, default);
            watch.Stop();
            Assert.True(result.Success, result.Error);
            output.WriteLine($"{scenario}: {watch.ElapsedMilliseconds} ms; {string.Join(", ", phases.Phases)}");
            var secondsPerClip = scenario == "cut" ? 3 : 4;
            var seconds = secondsPerClip * count;
            var rgb = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", result.FinalPath!, "-an", "-vf",
                "crop=2:2:4:4", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(seconds * 30 * 12, rgb.Length);
            for (var frame = 0; frame < seconds * 30; frame++)
                Assert.True(rgb[frame * 12 + (frame < secondsPerClip * 30 ? 0 : 2)] > 180, $"Wrong clip at frame {frame}");
            var pcm = await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", result.FinalPath!, "-vn",
                "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
            Assert.InRange(pcm.Length / 4, seconds * 48000, seconds * 48000 + 1024);
            for (var second = 0; second < seconds; second++)
            {
                var expected = second * 48000 + 12000;
                var peak = Enumerable.Range(expected - 960, 1921).MaxBy(n => Math.Abs(BitConverter.ToSingle(pcm, n * 4)));
                Assert.True(Math.Abs(BitConverter.ToSingle(pcm, peak * 4)) > .1);
                Assert.InRange(Math.Abs(peak - expected), 0, 96);
            }
            Assert.Equal(scenario is "cut" or "mixed-size", phases.Phases.Contains(RecordingExportPhase.Rendering));
        }
        finally { root.Delete(true); }
    }

    private sealed class PhaseRecorder : IRecordingExportProgress
    {
        public List<RecordingExportPhase> Phases { get; } = [];
        public void ReportPhase(RecordingExportPhase phase) => Phases.Add(phase);
        public void Report(double value) { }
    }
}
