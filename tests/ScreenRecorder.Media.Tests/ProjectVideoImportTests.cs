// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public class ProjectVideoImportTests
{
    [NativeMediaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedVideoPreservesAudioOffsetAndAppliesRotation(bool rotated)
    {
        var root = Directory.CreateTempSubdirectory("opencam-import-timing-").FullName;
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var input = Path.Combine(root, "offset.mp4");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=96x64:r=25:d=1",
                "-itsoffset", "0.2", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.8",
                "-c:v", "libx264", "-c:a", "aac", input]);
            if (rotated)
            {
                var rotation = Path.Combine(root, "rotation.mp4");
                await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-display_rotation", "90", "-i", input, "-c", "copy", rotation]);
                input = rotation;
            }
            IProjectMediaProcess process = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            await using var project = await new JsonProjectStore().CreateAsync(root, "Timing");
            var source = await new ProjectVideoImporter(process, new ProjectSourceProbe()).ImportAsync(project, input, null, default);
            Assert.Equal(rotated ? 64 : 96, source.Width);
            Assert.Equal(rotated ? 96 : 64, source.Height);
            using var owned = ProjectPathPolicy.OpenSource(project, source.RelativePath);
            using var json = System.Text.Json.JsonDocument.Parse((await process.RunAsync(ProjectMediaJob.InspectImport(), [owned], null, default)).Output);
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            double Start(string type) => double.Parse(streams.Single(s => s.GetProperty("codec_type").GetString() == type)
                .GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(Start("audio") - Start("video"), .17, .205);
        }
        finally { Directory.Delete(root, true); }
    }

    [NativeMediaTheory]
    [InlineData("mp4", "libx264", true)]
    [InlineData("mov", "mpeg4", true)]
    [InlineData("mkv", "libx264", false)]
    public async Task ImportedVideoIsOwnedPlayableAndPreservesOriginal(string extension, string codec, bool audio)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencam-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "教學示範." + extension);
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var args = new List<string> { "-f", "lavfi", "-i", "color=red:s=96x64:r=25:d=1" };
            if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1"]);
            args.AddRange(["-c:v", codec]);
            if (audio) args.AddRange(["-c:a", extension == "mov" ? "pcm_s16le" : "aac"]);
            args.Add(input);
            await RecordingContentExportIntegrationTests.Run(ffmpeg, args.ToArray());
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            await using var project = await new JsonProjectStore().CreateAsync(root, "Imported lesson");
            IProjectMediaProcess process = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            var importer = new ProjectVideoImporter(process, new ProjectSourceProbe());
            var source = await importer.ImportAsync(project, input, null, default);
            Assert.Equal("h264", source.VideoCodec);
            Assert.Equal(audio ? "aac" : null, source.AudioCodec);
            Assert.StartsWith("sources/", source.RelativePath);
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
            await using var owned = ProjectPathPolicy.OpenSource(project, source.RelativePath);
            if (codec == "libx264")
            {
                var before = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", input, "-map", "0:v:0", "-c:v", "copy", "-an", "-f", "h264", "pipe:1"]);
                var after = await RecordingContentExportIntegrationTests.Run(ffmpeg,
                    ["-i", Path.Combine(project.ProjectDirectory, source.RelativePath), "-map", "0:v:0", "-c:v", "copy", "-an", "-f", "h264", "pipe:1"]);
                Assert.Equal(SHA256.HashData(before), SHA256.HashData(after));
            }
            var frame = await process.RunAsync(ProjectMediaJob.ExtractFrame(source.Timing.StartPts,
                source.Timing.TimeBase, 96, 64), [owned], null, default);
            Assert.True(frame.Output[0] > 200 && frame.Output[1] < 40 && frame.Output[2] < 40);
            Assert.InRange((double)source.Timing.DurationTs * source.Timing.TimeBase.Numerator /
                source.Timing.TimeBase.Denominator, .95, 1.1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptVideoLeavesProjectAndOriginalUntouched()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "opencam-import-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "broken.mp4");
            await File.WriteAllBytesAsync(input, [1, 2, 3, 4]);
            await using var project = await new JsonProjectStore().CreateAsync(root, "Preserved");
            IProjectMediaProcess process = OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!)
                : new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!);
            var importer = new ProjectVideoImporter(process, new ProjectSourceProbe());
            await Assert.ThrowsAnyAsync<Exception>(() => importer.ImportAsync(project, input, null, default));
            Assert.Empty(project.Current.Sources);
            Assert.Empty(Directory.GetFiles(Path.Combine(project.ProjectDirectory, "sources")));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(input));
        }
        finally { Directory.Delete(root, true); }
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task VideoImportCancellationKeepsIpcResponsiveAndDoesNotStartRecording()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return;
        await using var scope = new RecordingScope();
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
        var input = Path.Combine(scope.Configuration.OutputDirectory, "cancel.mp4");
        await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=96x64:r=25:d=1",
            "-c:v", "libx264", input]);
        IProjectMediaProcess process = OperatingSystem.IsWindows()
            ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
        var blocked = new BlockImportProcess(process);
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            new JsonProjectStore(), new ProjectSourceProbe(), mediaProcess: () => blocked);
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Cancel import")).Success);
        var original = coordinator.Current!;
        var op = Guid.NewGuid();
        Assert.True((await coordinator.StartVideoImportAsync(input, original.Revision, op)).Success);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var status = await dispatcher.DispatchAsync(new() { MessageType = "GetProjectStatus", PayloadJson = "{}" })
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(status.Success);
        Assert.False((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.CancelVideoImportAsync(op)).Success);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.IsImporting) await Task.Delay(10, deadline.Token);
        Assert.Equal(ProjectVideoImportState.Canceled, coordinator.ImportStatus!.State);
        Assert.Equal(original, coordinator.Current);
        Assert.Empty(Directory.GetFiles(Path.Combine(coordinator.ProjectDirectory!, "sources")));
        Assert.True(File.Exists(input));
    }

    private sealed class BlockImportProcess(IProjectMediaProcess inner) : IProjectMediaProcess
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct)
        {
            if (output is not null)
            {
                Entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return await inner.RunAsync(job, inputs, output, ct);
        }
    }

    [NativeMediaTheory]
    [InlineData(20)]
    [InlineData(30)]
    public async Task VideoImportThroughEditorSupportsUndoReopenAndMixedExport(int fps)
    {
        await using var scope = new RecordingScope();
        var root = scope.Configuration.OutputDirectory;
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
        var red = Path.Combine(root, "red.mp4");
        var blue = Path.Combine(root, "blue.mov");
        await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=red:s=96x64:r=25:d=1",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1:sample_rate=48000", "-c:v", "libx264", "-c:a", "aac", red]);
        await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=blue:s=48x80:r=20:d=1",
            "-c:v", "mpeg4", blue]);
        var store = new JsonProjectStore();
        string manifest;
        await using (var owner = await store.CreateAsync(root, "Video import"))
        {
            await owner.SaveAsync(owner.Current with { Revision = 1, Canvas = new(96, 64, new(fps, 1)) }, 0);
            manifest = Path.Combine(owner.ProjectDirectory, "project.opencam");
        }
        IProjectMediaProcess CreateProcess() => OperatingSystem.IsWindows()
            ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            store, new ProjectSourceProbe(), mediaProcess: CreateProcess);
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var client = new ScreenRecorder.UI.Projects.ProjectClient((command, request, _) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) }));
        var vm = new ScreenRecorder.UI.Projects.ProjectWorkspaceViewModel(client);
        await vm.OpenAsync(manifest);
        await vm.ImportVideosAsync([red, blue]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(vm.Error);
        Assert.Equal(new[] { "red", "blue" }, vm.Clips.Select(c => c.Name));
        Assert.Equal(2 * TimeSpan.TicksPerSecond, ProjectTimeline.Build(coordinator.Current!).DurationTicks);
        Assert.False(vm.IsImporting);
        Assert.True(vm.CanUndo);
        var sourcePaths = coordinator.Current!.Sources.Select(s => Path.Combine(coordinator.ProjectDirectory!, s.RelativePath)).ToArray();
        await vm.UndoAsync();
        Assert.Single(vm.Clips);
        Assert.All(sourcePaths, p => Assert.True(File.Exists(p)));
        await vm.RedoAsync();
        Assert.Equal(2, vm.Clips.Count);
        await vm.SaveAsync();
        Assert.True(await vm.CloseAsync());
        Assert.Null(vm.State.VideoImport);
        File.Move(red, red + ".moved");
        File.Move(blue, blue + ".moved");
        await vm.OpenAsync(manifest);
        Assert.Null(vm.Error);
        Assert.Equal(2, vm.Clips.Count);
        Assert.True(await vm.CloseAsync());
        await using var reopened = await store.OpenAsync(manifest);
        var result = await new ScreenRecorder.Recorder.Services.ProjectFfmpegExporter(CreateProcess()).ExportAsync(reopened,
            reopened.Current, root, Guid.NewGuid(), new Progress<double>(), default);
        Assert.True(result.Success, result.Error);
        var pixels = await RecordingContentExportIntegrationTests.Run(ffmpeg,
            ["-i", result.FinalPath!, "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
        Assert.Equal(fps * 2 * 96 * 64 * 3, pixels.Length);
        var first = (32 * 96 + 48) * 3;
        var second = (fps * 96 * 64 + 32 * 96 + 48) * 3;
        Assert.True(pixels[first] > 200 && pixels[first + 2] < 40);
        Assert.True(pixels[second + 2] > 200 && pixels[second] < 40);
        var audio = await RecordingContentExportIntegrationTests.Run(ffmpeg,
            ["-i", result.FinalPath!, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1"]);
        Assert.InRange(audio.Length / 4, 96000, 97024);
        var firstPeak = 0f;
        var lastPeak = 0f;
        for (var i = 10000; i < 40000; i++) firstPeak = Math.Max(firstPeak, Math.Abs(BitConverter.ToSingle(audio, i * 4)));
        for (var i = 60000; i < 90000; i++) lastPeak = Math.Max(lastPeak, Math.Abs(BitConverter.ToSingle(audio, i * 4)));
        Assert.True(firstPeak > .01f);
        Assert.True(lastPeak < .001f);
    }
}
