// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class ProjectConcatExportTests
{
    [NativeMediaTheory]
    [InlineData(20)][InlineData(30)]
    public async Task FiveMinuteAudioEditKeepsVideoTimeline(int fps)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-long-audio-export-");
        try
        {
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "Five minute export");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var original = Path.Combine(owner.ProjectDirectory, "sources/long.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-f", "lavfi", "-i", $"testsrc2=s=64x36:r={fps}:d=300", "-f", "lavfi", "-i",
                    "sine=frequency=440:sample_rate=48000:duration=300", "-c:v", "libx264", "-preset", "veryfast",
                    "-bf", "0", "-c:a", "aac", "-ac", "2", original]);
            IProjectMediaProcess media = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            ProjectSourceEvidence evidence;
            await using (var input = File.OpenRead(original))
                evidence = await ProjectPacketInspector.InspectAsync((IProjectStreamingMediaProcess)media, input, default);
            var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "s", RelativePath = "sources/long.mkv",
                Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "aac", FileSize = new FileInfo(original).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(original))),
                Timing = new(evidence.Video.TimeBase, evidence.Video.FirstPts, evidence.Video.EndPts - evidence.Video.FirstPts) };
            var project = owner.Current with { Revision = 1, Sessions = ["s"], Sources = [source],
                Canvas = new(64, 36, new(fps, 1)), Clips = [new() { Id = Guid.NewGuid(), SourceId = source.Id,
                    Name = "half volume", Volume = .5, InPts = source.Timing.StartPts,
                    OutPts = source.Timing.StartPts + source.Timing.DurationTs }] };
            await owner.SaveAsync(project, 0);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var progress = new PhaseProgress(_ => { });
            var result = await new ProjectFfmpegExporter(media).ExportAsync(owner, project, destination,
                Guid.NewGuid(), progress, default);
            Assert.True(result.Success, result.Error);
            Assert.DoesNotContain(RecordingExportPhase.Rendering, progress.Phases);
            await using (var output = File.OpenRead(result.FinalPath!))
            {
                var actual = await ProjectPacketInspector.InspectAsync((IProjectStreamingMediaProcess)media, output, default);
                Assert.Equal(300 * fps, actual.Video.PacketCount);
                Assert.Equal(evidence.Video.PacketHash, actual.Video.PacketHash);
                Assert.True(actual.Video.MonotonicDts);
                Assert.True(actual.Audio!.MonotonicDts);
                Assert.InRange(Math.Abs(actual.Video.EndSeconds - 300), 0, .002);
                Assert.InRange(Math.Abs(actual.Audio.EndSeconds - 300), 0, .025);
            }
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-i", result.FinalPath!, "-f", "null", "-"]);
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(original))));
            Assert.Single(Directory.GetFiles(destination));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void NormalizedAudioKeepsExistingExportBitrate()
    {
        var arguments = ProjectMediaJob.MuxCopiedVideoAndAudio(1024).FileDescriptorArguments();
        Assert.Contains("-b:a", arguments);
        Assert.Equal("192k", arguments[Array.IndexOf(arguments, "-b:a") + 1]);
    }

    [NativeMediaTheory]
    [InlineData(1, "volume")][InlineData(2, "volume")]
    [InlineData(1, "mute")][InlineData(2, "mute")]
    [InlineData(1, "fade")][InlineData(2, "fade")]
    public async Task AudioOnlyEditsPreserveVideoAndChangeDecodedSound(int count, string effect)
    {
        using var diagnostics = new ExportDiagnostics();
        var root = Directory.CreateTempSubdirectory("OpenCam-audio-edit-");
        try
        {
            await using var owner = await RecordingContentExportIntegrationTests.SingleClip(root.FullName, audio: true);
            var original = Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv");
            var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(original));
            var clip = owner.Current.Clips[0];
            clip = effect switch { "mute" => clip with { Muted = true },
                "fade" => clip with { FadeInTicks = 1000000, FadeOutTicks = 1000000 },
                _ => clip with { Volume = .5 } };
            var snapshot = owner.Current with { Revision = owner.Current.Revision + 1,
                Clips = [..Enumerable.Range(0, count).Select(i => i == 0 ? clip with { Id = Guid.NewGuid() } :
                    effect switch {
                        "mute" => clip with { Id = Guid.NewGuid(), Muted = false },
                        "fade" => clip with { Id = Guid.NewGuid(), FadeInTicks = 500000, FadeOutTicks = 500000 },
                        _ => clip with { Id = Guid.NewGuid(), Volume = .25 }
                    })] };
            await owner.SaveAsync(snapshot, owner.Current.Revision);
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess media = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            var progress = new PhaseProgress(_ => { });
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var result = await new ProjectFfmpegExporter(media).ExportAsync(owner, snapshot, destination,
                Guid.NewGuid(), progress, default);
            Assert.True(result.Success, result.Error);
            Assert.Contains(RecordingExportPhase.ConvertingAudio, progress.Phases);
            Assert.DoesNotContain(RecordingExportPhase.Rendering, progress.Phases);
            var sourcePackets = await VideoPacketHashes(original);
            Assert.Equal(Enumerable.Range(0, count).SelectMany(_ => sourcePackets), await VideoPacketHashes(result.FinalPath!));
            await using (var output = File.OpenRead(result.FinalPath!))
            {
                var evidence = await ProjectPacketInspector.InspectAsync((IProjectStreamingMediaProcess)media, output, default);
                Assert.True(evidence.Video.MonotonicDts);
                Assert.True(evidence.Audio!.MonotonicDts);
                Assert.InRange(Math.Abs(evidence.Video.EndSeconds - evidence.Audio.EndSeconds), 0, .025);
            }
            async Task<float[]> Audio(string path)
            {
                var bytes = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", path, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
                var samples = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                return samples;
            }
            var audio = await Audio(result.FinalPath!);
            var sourceAudio = await Audio(original);
            static double Rms(float[] data, int offset, int length) =>
                Math.Sqrt(data.Skip(offset).Take(length).Select(x => (double)x * x).Average());
            for (var segment = 0; segment < count; segment++)
            {
                var offset = segment * 14400;
                if (effect == "mute" && segment == 0)
                    Assert.InRange(Rms(audio, offset + 4800, 2400), 0, .00001);
                else if (effect != "fade")
                {
                    var expected = effect == "mute" ? 1 : segment == 0 ? .5 : .25;
                    Assert.InRange(Rms(audio, offset + 4800, 2400) / Rms(sourceAudio, 4800, 2400), expected - .025, expected + .025);
                }
                else
                {
                    Assert.True(Rms(audio, offset, 480) < Rms(audio, offset + 5760, 960) * .4);
                    Assert.True(Rms(audio, offset + 13920, 480) < Rms(audio, offset + 5760, 960) * .4);
                }
            }
            Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(original)));
            Assert.Single(Directory.GetFiles(destination));
            Assert.Contains("Project export phase", diagnostics.ToString());
            Assert.Contains("Project export timing", diagnostics.ToString());
            foreach (var step in new[] { "CopyVideo", "RenderAudio", "Mux" })
                Assert.Contains(diagnostics.Entries, entry => entry.Properties.TryGetValue("Step", out var value) &&
                    value is Serilog.Events.ScalarValue scalar && Equals(step, scalar.Value));
            var completed = Assert.Single(diagnostics.Entries.Where(entry => entry.Properties.ContainsKey("Completed")));
            Assert.Equal(true, Assert.IsType<Serilog.Events.ScalarValue>(completed.Properties["Completed"]).Value);
        }
        finally { root.Delete(true); }
    }
}
