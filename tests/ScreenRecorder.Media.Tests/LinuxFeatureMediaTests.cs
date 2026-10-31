// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxFeatureMediaTests
{
    [LinuxOnlyFact]
    public async Task ImportedVideoSubtitlesGifAndWebExportUseLinuxBoundDescriptorsAndPreserveOriginal()
    {
        var root = Directory.CreateTempSubdirectory("opencam-linux-features-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var original = Path.Combine(root.FullName, "教學影片.mp4");
            await RecordingContentExportIntegrationTests.Run(ffmpeg,
                ["-f", "lavfi", "-i", "testsrc2=s=96x64:r=20:d=2", "-f", "lavfi", "-i",
                 "sine=frequency=440:sample_rate=48000:duration=2", "-c:v", "libx264", "-bf", "0", "-c:a", "aac", original]);
            var before = SHA256.HashData(await File.ReadAllBytesAsync(original));
            var process = new LinuxProjectMediaProcess(ffmpeg);
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName, "Linux lesson");
            var source = await new ProjectVideoImporter(process, new ProjectSourceProbe()).ImportAsync(owner, original, null, default);
            Assert.Equal("h264", source.VideoCodec);
            Assert.Equal("aac", source.AudioCodec);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(original)));
            var subtitlePath = Path.Combine(root.FullName, "繁體中文.srt");
            await File.WriteAllTextAsync(subtitlePath, "1\n00:00:00,000 --> 00:00:01,000\nLinux 教材\n");
            using var subtitle = File.OpenRead(subtitlePath);
            var track = await ProjectSubtitleStore.ImportAsync(owner, subtitle, "繁體中文.srt", "zh-TW");
            var snapshot = owner.Current with {
                SchemaVersion = 4, Revision = 1, Canvas = new(96, 64, new(20, 1)), Sources = [source], Sessions = ["imported"],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = source.Id, Name = "匯入教材", InPts = source.Timing.StartPts,
                    OutPts = source.Timing.StartPts + source.Timing.DurationTs }], Subtitles = new([track], track.Id)
            };
            await owner.SaveAsync(snapshot, 0);
            var output = Directory.CreateDirectory(Path.Combine(root.FullName, "exports")).FullName;
            var gif = await new ProjectGifExporter(process, new()).ExportAsync(owner, owner.Current, output, Guid.NewGuid(), new Progress<double>(), default);
            Assert.True(gif.Success, gif.Error);
            using (var file = File.OpenRead(gif.FinalPath!))
            {
                var info = GifStructureReader.Read(file);
                Assert.True(info.FrameCount >= 20);
                Assert.InRange(info.DurationCentiseconds, 190, 210);
            }
            var web = await new ProjectWebPlayerExporter(new ProjectFfmpegExporter(process)).ExportAsync(
                owner, owner.Current, output, Guid.NewGuid(), new Progress<double>(), default);
            Assert.True(web.Success, web.Error);
            var folder = Path.GetDirectoryName(web.FinalPath!)!;
            Assert.True(File.Exists(Path.Combine(folder, "media.mp4")));
            var script = await File.ReadAllTextAsync(Path.Combine(folder, "opencam-player.js"));
            using var data = JsonDocument.Parse(script[(script.IndexOf(" = ", StringComparison.Ordinal) + 3)..].Trim().TrimEnd(';'));
            var exportedTrack = Assert.Single(data.RootElement.GetProperty("subtitles").GetProperty("tracks").EnumerateArray());
            Assert.Equal("Linux 教材", exportedTrack.GetProperty("cues")[0].GetProperty("text").GetString());
            Assert.Single(data.RootElement.GetProperty("chapters").EnumerateArray());
            // Complete decoding, not just an existing output filename or a still frame.
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-v", "error", "-i", gif.FinalPath!, "-f", "null", "-"]);
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-v", "error", "-i", Path.Combine(folder, "media.mp4"), "-f", "null", "-"]);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(original)));
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(owner.ProjectDirectory, source.RelativePath)))));
        }
        finally { root.Delete(true); }
    }
}
