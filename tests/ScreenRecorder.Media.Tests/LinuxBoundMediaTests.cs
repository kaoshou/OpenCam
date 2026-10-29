// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Tests;
public sealed class LinuxOnlyFactAttribute : FactAttribute { public LinuxOnlyFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux/glibc/native shim"; } }
public sealed class LinuxBoundMediaTests
{
    [LinuxOnlyFact]
    public async Task RunningChildCancellationReapsItAndQuotaOverflowFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencam-linux-child-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            var inputPath = Path.Combine(directory, "input"); await File.WriteAllTextAsync(inputPath, "source");
            var script = Path.Combine(directory, "child"); var marker = Path.Combine(directory, "pid");
            await File.WriteAllTextAsync(script, "#!/bin/sh\necho $$ > '" + marker + "'\nexec /bin/sleep 30\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using var cancel = new CancellationTokenSource();
            var operation = new LinuxProjectMediaProcess(script).RunAsync(ProjectMediaJob.ExtractFrame(0, new(1, 1000), 1, 1), [input], null, cancel.Token);
            for (var i = 0; i < 200 && !File.Exists(marker); ++i) await Task.Delay(10);
            Assert.True(File.Exists(marker)); var pid = int.Parse(await File.ReadAllTextAsync(marker)); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
            await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 12345678901234567890\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => new LinuxProjectMediaProcess(script).RunAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 1, 1), [input], null, default));
            Assert.Equal("source", await File.ReadAllTextAsync(inputPath));
        } finally { Directory.Delete(directory, true); }
    }
    [LinuxOnlyFact]
    public async Task DecoderReadsPinnedInodeAfterPathReplacementAndDoesNotModifyInput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencam-linux-fd-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!; var path = Path.Combine(directory, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=c=red:s=64x36:d=1:r=20", "-c:v", "libx264", "-pix_fmt", "yuv420p", path]);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            File.Move(path, path + ".original"); await File.WriteAllTextAsync(path, "replacement is not media");
            var decoded = await new LinuxProjectMediaProcess(ffmpeg).RunAsync(ProjectMediaJob.ExtractFrame(0, new(1, 1000), 64, 36), [input], null, default);
            Assert.Equal(64 * 36 * 4, decoded.Output.Length);
            Assert.True(decoded.Output[0] > 200 && decoded.Output[1] < 30 && decoded.Output[2] < 30);
            Assert.Equal("replacement is not media", await File.ReadAllTextAsync(path));
            using var writable = new FileStream(path + ".original", FileMode.Open, FileAccess.ReadWrite);
            await Assert.ThrowsAsync<ArgumentException>(() => new LinuxProjectMediaProcess(ffmpeg).RunAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 64, 36), [writable], null, default));
        } finally { Directory.Delete(directory, true); }
    }
    [LinuxOnlyFact]
    public async Task CanceledBoundOperationDoesNotStartOrWriteOutput()
    {
        var path = Path.GetTempFileName();
        try {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LinuxProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 64, 36), [input], null, cts.Token));
        } finally { File.Delete(path); }
    }
}
