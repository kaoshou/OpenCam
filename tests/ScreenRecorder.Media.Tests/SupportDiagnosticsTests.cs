// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using ScreenRecorder.UI.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class SupportDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCam-support-tests-" + Guid.NewGuid());
    private readonly DateTime _today = new(2026, 11, 4);
    public SupportDiagnosticsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task BundleContainsOnlyRecentBoundedAppLogsAndExplicitEnvironmentSummary()
    {
        var logs = Path.Combine(_root, "Logs"); Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(Path.Combine(logs, "app-20261104.log"), "normal log");
        await File.WriteAllTextAsync(Path.Combine(logs, "app-20261001.log"), "old log");
        await File.WriteAllTextAsync(Path.Combine(logs, "project.opencam"), "PRIVATE PROJECT");
        await File.WriteAllTextAsync(Path.Combine(logs, "media.mp4"), "PRIVATE MEDIA");
        await File.WriteAllTextAsync(Path.Combine(logs, "app-20261104.log.srt"), "PRIVATE SUBTITLE");
        var output = Path.Combine(_root, "support.zip");
        await SupportDiagnostics.ExportAsync(logs, output, "known environment", _today);
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal(new[] { "environment.txt", "logs/app-20261104.log", "README.txt" }.Order(), archive.Entries.Select(e => e.FullName).Order());
        using var reader = new StreamReader(archive.GetEntry("environment.txt")!.Open());
        Assert.Equal("known environment", await reader.ReadToEndAsync());
        await Assert.ThrowsAsync<IOException>(() => SupportDiagnostics.ExportAsync(logs, output, "overwrite", _today));
        Assert.Equal("normal log", await File.ReadAllTextAsync(Path.Combine(logs, "app-20261104.log")));
    }

    [Fact]
    public async Task OversizedLogsAreOmittedAndLinksNeverIncludeTheirTargets()
    {
        var logs = Path.Combine(_root, "Logs"); Directory.CreateDirectory(logs);
        using (var file = File.Create(Path.Combine(logs, "app-20261104.log"))) file.SetLength(SupportDiagnostics.MaximumLogBytes + 1);
        var secret = Path.Combine(_root, "secret.txt"); await File.WriteAllTextAsync(secret, "DO NOT EXPORT");
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(logs, "app-20261103.log"), secret);
        var output = Path.Combine(_root, "support.zip");
        await SupportDiagnostics.ExportAsync(logs, output, "environment", _today);
        using var archive = ZipFile.OpenRead(output);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("logs/"));
    }

    [Fact]
    public void SummaryDoesNotContainUserDirectoriesOrMachineName()
    {
        var summary = SupportDiagnostics.EnvironmentSummary();
        Assert.Contains("OpenCam", summary); Assert.Contains("OBS/libobs", summary);
        Assert.Contains("Architecture:", summary); Assert.Contains(".NET:", summary);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), summary);
        Assert.DoesNotContain(Environment.MachineName, summary);
    }

    [Fact]
    public async Task ActiveLogCanBeReadWithoutBlockingItsWriter()
    {
        var logs = Path.Combine(_root, "Logs"); Directory.CreateDirectory(logs);
        using var writer = new FileStream(Path.Combine(logs, "app-20261104.log"), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        await writer.WriteAsync("active log"u8.ToArray()); await writer.FlushAsync();
        var output = Path.Combine(_root, "support.zip");
        await SupportDiagnostics.ExportAsync(logs, output, "environment", _today);
        using var archive = ZipFile.OpenRead(output);
        using var reader = new StreamReader(archive.GetEntry("logs/app-20261104.log")!.Open());
        Assert.Equal("active log", await reader.ReadToEndAsync());
        await writer.WriteAsync(" still writing"u8.ToArray());
    }

    [Fact]
    public async Task CancellationRemovesOnlyItsIncompleteArchive()
    {
        var output = Path.Combine(_root, "support.zip");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SupportDiagnostics.ExportAsync(_root, output, "environment", _today, cancel.Token));
        Assert.False(File.Exists(output));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task HardLinkedLogIsOmittedWithoutLosingTheRemainingBundle()
    {
        var logs = Path.Combine(_root, "Logs"); Directory.CreateDirectory(logs);
        var secret = Path.Combine(_root, "secret.txt"); await File.WriteAllTextAsync(secret, "PRIVATE HARD LINK");
        var linked = Path.Combine(logs, "app-20261104.log");
        Assert.True(OperatingSystem.IsWindows() ? CreateHardLink(linked, secret, IntPtr.Zero) : link(secret, linked) == 0);
        await File.WriteAllTextAsync(Path.Combine(logs, "app-20261103.log"), "safe log");
        var output = Path.Combine(_root, "support.zip");
        await SupportDiagnostics.ExportAsync(logs, output, "environment", _today);
        using var archive = ZipFile.OpenRead(output);
        Assert.Null(archive.GetEntry("logs/app-20261104.log"));
        Assert.NotNull(archive.GetEntry("logs/app-20261103.log"));
        Assert.NotNull(archive.GetEntry("environment.txt"));
        Assert.Equal("PRIVATE HARD LINK", await File.ReadAllTextAsync(secret));
    }

    [Fact]
    public async Task MissingLogDirectoryStillProducesUsableEnvironmentBundle()
    {
        var output = Path.Combine(_root, "support.zip");
        await SupportDiagnostics.ExportAsync(Path.Combine(_root, "missing"), output, "environment", _today);
        using var archive = ZipFile.OpenRead(output); Assert.Equal(2, archive.Entries.Count);
    }
    public void Dispose() => Directory.Delete(_root, true);
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string destination);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string destination, string existing, IntPtr reserved);
}
