// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectRecordingIntegrationTests
{
    [Fact]
    public async Task RealFfmpegSyntheticSources_ThreeClipsRestartProcessAppendMove_PreservesMediaWithoutMp4()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-project-integration-");
        try
        {
            await RunChildAsync("create", directory.FullName);
            var manifest = Assert.Single(Directory.GetFiles(directory.FullName, "project.opencam", SearchOption.AllDirectories));
            string[] originalHashes;
            await using (var first = await new JsonProjectStore().OpenAsync(manifest))
            {
                Assert.Equal(3, first.Current.Sources.Length);
                originalHashes = first.Current.Sources.Select(s => s.Sha256).ToArray();
            }
            await RunChildAsync("append", manifest);
            var moved = Path.Combine(directory.FullName, "moved-project");
            Directory.Move(Path.GetDirectoryName(manifest)!, moved);
            await using var project = await new JsonProjectStore().OpenAsync(Path.Combine(moved, "project.opencam"));
            Assert.Equal(4, project.Current.Sources.Length);
            Assert.Equal(4, project.Current.Clips.Length);
            Assert.Equal(originalHashes, project.Current.Sources.Take(3).Select(s => s.Sha256));
            foreach (var source in project.Current.Sources)
            {
                using var stream = ProjectPathPolicy.OpenSource(project, source.RelativePath);
                Assert.Equal(source.Sha256, Convert.ToHexString(await SHA256.HashDataAsync(stream)));
                stream.Position = 0;
                var info = await new ProjectSourceProbe().ProbeAsync(stream);
                Assert.True(info.Timing.DurationTs > 0);
                Assert.Equal(source.Timing, info.Timing);
            }
            Assert.Empty(Directory.GetFiles(directory.FullName, "*.mp4", SearchOption.AllDirectories));
        }
        finally { directory.Delete(true); }
    }

    private static async Task RunChildAsync(string mode, string path)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "project-recording-probe", "ScreenRecorder.ProjectRecordingProbe.dll"));
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }
}
