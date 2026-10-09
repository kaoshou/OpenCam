// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingContentExportIntegrationTests
{
    [MacOsOnlyFact]
    public async Task CleanupFailureCannotPublishAnOutputThenReportFailure()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-cleanup-export-");
        try
        {
            await using var owner = await SingleClip(root.FullName);
            // This fault targets the exact-render intermediate, not the remux route.
            await owner.SaveAsync(owner.Current with { Revision = owner.Current.Revision + 1,
                Clips = [owner.Current.Clips[0] with { Scale = 2 }] }, owner.Current.Revision);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            await Assert.ThrowsAnyAsync<IOException>(() => exporter.ExportAsync(owner, owner.Current,
                destination, Guid.NewGuid(), new InlineProgress(progress =>
                {
                    if (progress != .9) return;
                    // Simulate external interference with a completed, closed intermediate.
                    var intermediate = Directory.GetFiles(destination, "*.h264").Single();
                    File.Delete(intermediate);
                    Directory.CreateDirectory(intermediate);
                }), default));
            Assert.Empty(Directory.GetFiles(destination, "OpenCam_*.mp4"));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task EditedTimelineExportsRetainedIntervalsInOrderWithAudioAndUnchangedSources()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-edited-export-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "Export acceptance");
            var sources = new List<ProjectSource>();
            foreach (var (color, tone) in new[] { ("red", 440), ("green", 660), ("blue", 880) })
            {
                var relative = $"sources/{color}.mkv";
                var path = Path.Combine(owner.ProjectDirectory, relative);
                await Run(ffmpeg, ["-f", "lavfi", "-i", $"color={color}:s=64x36:r=30:d=1",
                    "-f", "lavfi", "-i", $"sine=frequency={tone}:sample_rate=48000:duration=1",
                    "-vf", (color == "green" ? "drawbox=c=white:t=fill:enable='gte(t,0.2)*lt(t,0.6)'," : "") + "setpts=PTS+2/TB",
                    "-af", "asetpts=PTS+2/TB", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_s16le", path]);
                sources.Add(new() { Id = Guid.NewGuid(), SessionId = "fixture", RelativePath = relative,
                    Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "pcm_s16le",
                    Timing = new(new(1, 1000), 2000, 1000), FileSize = new FileInfo(path).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))) });
            }
            ProjectClip Clip(int index, long start, long end) => new() { Id = Guid.NewGuid(),
                SourceId = sources[index].Id, InPts = start, OutPts = end, Name = $"clip {index}" };
            // C / A / B-left / B-right: B's middle 400 ms never enters the output.
            var project = owner.Current with { Revision = 1, Canvas = new(64, 36, new(30, 1)),
                Sessions = ["fixture"], Sources = [..sources], Clips = [Clip(2, 2200, 2400) with { Muted = true },
                    Clip(0, 2100, 2500) with { Volume = 0.5 }, Clip(1, 2000, 2200), Clip(1, 2600, 3000)] };
            await owner.SaveAsync(project, 0);
            var output = Path.Combine(root.FullName, "exports");
            Directory.CreateDirectory(output);
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(ffmpeg));
            var result = await exporter.ExportAsync(owner, project, output, Guid.NewGuid(), new Progress<double>(), default);
            Assert.True(result.Success, result.Error);
            Assert.NotNull(result.FinalPath);
            // Independent full decode: 36 actual frames, exact C/A/B ordering.
            var pixels = await Run(ffmpeg, ["-i", result.FinalPath!, "-map", "0:v:0", "-an", "-pix_fmt", "rgb24",
                "-c:v", "rawvideo", "-threads", "1", "-f", "rawvideo", "pipe:1"]);
            Assert.Equal(36 * 64 * 36 * 3, pixels.Length);
            for (var frame = 0; frame < 36; frame++)
            {
                var offset = frame * 64 * 36 * 3;
                var channel = frame < 6 ? 2 : frame < 18 ? 0 : 1;
                Assert.True(pixels[offset + channel] > 100);
                Assert.True(pixels[offset + (channel + 1) % 3] < 20);
            }
            var audio = await Run(ffmpeg, ["-i", result.FinalPath!, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
            double Rms(int start, int count) => Math.Sqrt(Enumerable.Range(start, count)
                .Select(i => Math.Pow(BitConverter.ToSingle(audio, i * 4), 2)).Average());
            Assert.InRange(audio.Length / 4, 57600, 58624); // AAC terminal padding, not a gap at every join.
            Assert.True(Rms(0, 8000) < 0.001); // C muted.
            Assert.InRange(Rms(12000, 4000), 0.03, 0.06); // A half-volume.
            Assert.InRange(Rms(34000, 4000), 0.07, 0.11); // B original level.
            foreach (var source in sources)
                Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(
                    await File.ReadAllBytesAsync(Path.Combine(owner.ProjectDirectory, source.RelativePath)))));
            Assert.Single(Directory.GetFiles(output)); // No intermediate files remain after success.
            await using var verified = File.OpenRead(result.FinalPath!);
            var wrongPlan = ProjectRenderPlan.Create(project with { Clips = project.Clips.RemoveAt(0) });
            await Assert.ThrowsAsync<InvalidDataException>(() => ProjectOutputVerifier.VerifyAsync(
                new MacProjectMediaProcess(ffmpeg), verified, wrongPlan, default));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task CanceledExportRemovesOnlyOwnedTemporariesAndPreservesOldOutput()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-canceled-export-");
        try
        {
            await using var owner = await SingleClip(root.FullName);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var old = Path.Combine(destination, "existing.mp4");
            await File.WriteAllTextAsync(old, "previous output must survive");
            using var cancel = new CancellationTokenSource();
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(owner, owner.Current,
                destination, Guid.NewGuid(), new InlineProgress(_ => cancel.Cancel()), cancel.Token));
            Assert.Equal(new[] { old }, Directory.GetFiles(destination));
            Assert.Equal("previous output must survive", await File.ReadAllTextAsync(old));
            Assert.True(File.Exists(Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv")));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task ReplacedOutputParentCannotRedirectPublicationOrCleanup()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pinned-export-");
        try
        {
            await using var owner = await SingleClip(root.FullName);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var moved = destination + "-original";
            var replaced = false;
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            var result = await exporter.ExportAsync(owner, owner.Current, destination, Guid.NewGuid(), new InlineProgress(_ =>
            {
                if (replaced) return;
                Directory.Move(destination, moved);
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "keep.txt"), "replacement");
                replaced = true;
            }), default);
            Assert.True(result.Success, result.Error);
            Assert.Equal(Path.GetFullPath(moved).Replace("/var/", "/private/var/"), Path.GetDirectoryName(result.FinalPath!));
            Assert.Single(Directory.GetFiles(moved));
            Assert.Equal("replacement", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
            Assert.Single(Directory.GetFiles(destination));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task ChangedSourceFailsWithoutPublishingAnMp4()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-source-export-");
        try
        {
            await using var owner = await SingleClip(root.FullName);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            await File.AppendAllTextAsync(Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv"), "changed");
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            await Assert.ThrowsAsync<InvalidDataException>(() => exporter.ExportAsync(owner, owner.Current,
                destination, Guid.NewGuid(), new Progress<double>(), default));
            Assert.Empty(Directory.GetFiles(destination));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task CompatibleWholeRecordingKeepsEncodedVideoPackets()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-copy-export-");
        try
        {
            await using var owner = await SingleClip(root.FullName, audio: true);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var media = new MacProjectMediaProcess(ffmpeg);
            var plan = ProjectRenderPlan.Create(owner.Current);
            var copied = Path.Combine(root.FullName, "diagnostic.mp4");
            await using (var input = File.OpenRead(Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv")))
            await using (var output = new FileStream(copied, FileMode.CreateNew, FileAccess.ReadWrite))
                await media.RunAsync(ProjectMediaJob.CopyWholeRecording(plan)!, [input], output, default);
            await using (var output = File.OpenRead(copied))
            {
                var info = await media.RunAsync(ProjectMediaJob.InspectEditedMp4(), [output], null, default);
                try { await ProjectOutputVerifier.VerifyAsync(media, output, plan, default); }
                catch (Exception ex) { Assert.Fail(ex.Message + "\n" + System.Text.Encoding.UTF8.GetString(info.Output)); }
            }
            var exporter = new ProjectFfmpegExporter(new MacProjectMediaProcess(ffmpeg));
            var result = await exporter.ExportAsync(owner, owner.Current, destination, Guid.NewGuid(), new Progress<double>(), default);
            var original = Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv");
            // Raw H.264 stream copy preserves packet payloads (including SPS/SEI); re-encoding doesn't.
            Task<byte[]> Encoded(string path) => Run(ffmpeg, ["-i", path, "-map", "0:v:0", "-c:v", "copy", "-an",
                "-bsf:v", "h264_mp4toannexb", "-f", "h264", "pipe:1"]);
            Assert.Equal(await Encoded(original), await Encoded(result.FinalPath!));
        }
        finally { root.Delete(true); }
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    { public void Report(double value) => report(value); }

    private static async Task<IProjectHandle> SingleClip(string root, bool audio = false)
    {
        var owner = await new JsonProjectStore().CreateAsync(root, "export fixture");
        try
        {
            var path = Path.Combine(owner.ProjectDirectory, "sources/fixture.mkv");
            await Run(FFmpegDiscovery.FindFFmpegExecutable()!, ["-f", "lavfi", "-i", "testsrc2=s=64x36:r=30:d=0.3",
                ..(audio ? new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.3", "-ac", "2", "-c:a", "aac" } : new[] { "-an" }),
                "-c:v", "libx264", "-bf", "0", path]);
            var id = Guid.NewGuid();
            await owner.SaveAsync(owner.Current with { Revision = 1, Canvas = new(64, 36, new(30, 1)), Sessions = ["s"],
                Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/fixture.mkv", Width = 64, Height = 36,
                    VideoCodec = "h264", AudioCodec = audio ? "aac" : null,
                    Timing = new(new(1, 1000), 0, 300), FileSize = new FileInfo(path).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))) }],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "fixture", InPts = 0, OutPts = 300 }] }, 0);
            return owner;
        }
        catch { await owner.DisposeAsync(); throw; }
    }

    internal static async Task<byte[]> Run(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-n" }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancel = timeout.Token.Register(() => { try { process.Kill(); } catch (InvalidOperationException) { } });
        var error = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await error);
        return output.ToArray();
    }
}
