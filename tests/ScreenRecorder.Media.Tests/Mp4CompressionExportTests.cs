// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class Mp4CompressionExportTests
{
    [MacOsOnlyTheory]
    [InlineData(Mp4ExportQuality.Standard, Mp4ExportResolution.FullHd, false, 1920, 1080, "23")]
    [InlineData(Mp4ExportQuality.Compact, Mp4ExportResolution.Hd, true, 1280, 720, "28")]
    public async Task ChosenProfileActuallyRendersVerifiedMp4OrWebMediaWithoutChangingOriginals(
        Mp4ExportQuality quality, Mp4ExportResolution resolution, bool web, int width, int height, string crf)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-mp4-profile-");
        try
        {
            await using var owner = await RecordingContentExportIntegrationTests.SingleClip(root.FullName, audio: true);
            await owner.SaveAsync(owner.Current with { Revision = owner.Current.Revision + 1,
                Canvas = new(2560, 1440, new(30, 1)) }, owner.Current.Revision);
            var snapshot = owner.Current;
            var process = new ProfileProcess(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            var ctor = typeof(ProjectFfmpegExporter).GetConstructor([typeof(IProjectMediaProcess), typeof(Mp4ExportOptions)]);
            Assert.NotNull(ctor);
            IRecordingContentExporter exporter = (ProjectFfmpegExporter)ctor.Invoke([process, new Mp4ExportOptions(quality, resolution)]);
            if (web) exporter = new ProjectWebPlayerExporter(exporter);
            var output = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var oldOutput = Path.Combine(output, "existing.mp4");
            await File.WriteAllTextAsync(oldOutput, "keep");
            var result = await exporter.ExportAsync(owner, snapshot, output, Guid.NewGuid(), new Progress<double>(), default);
            Assert.True(result.Success, result.Error);
            var media = web ? Path.Combine(Path.GetDirectoryName(result.FinalPath)!, "media.mp4") : result.FinalPath!;
            await using var mediaStream = File.OpenRead(media);
            var metadata = await process.RunAsync(ProjectMediaJob.InspectEditedMp4(), [mediaStream], null, default);
            using var json = System.Text.Json.JsonDocument.Parse(metadata.Output);
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "video");
            Assert.Equal(width, video.GetProperty("width").GetInt32());
            Assert.Equal(height, video.GetProperty("height").GetInt32());
            Assert.Equal("9", video.GetProperty("nb_read_frames").GetString());
            Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "audio");
            Assert.Contains(crf, process.Crfs);
            Assert.Equal(snapshot, owner.Current);
            Assert.Equal("keep", await File.ReadAllTextAsync(oldOutput));
            var source = snapshot.Sources[0];
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                Path.Combine(owner.ProjectDirectory, source.RelativePath)))));
            Assert.Empty(Directory.GetFiles(output, ".opencam-export-*"));
        }
        finally { root.Delete(true); }
    }

    private sealed class ProfileProcess(IProjectMediaProcess inner) : IProjectMediaProcess
    {
        public List<string> Crfs { get; } = [];
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct)
        {
            var arguments = job.FileDescriptorArguments();
            var crf = Array.IndexOf(arguments, "-crf");
            if (crf >= 0) Crfs.Add(arguments[crf + 1]);
            return inner.RunAsync(job, inputs, output, ct);
        }
    }
}
