// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectPathSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCamProjectSecurity-" + Guid.NewGuid().ToString("N"));
    public ProjectPathSecurityTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("../outside.mkv")]
    [InlineData("/tmp/outside.mkv")]
    [InlineData("C:\\outside.mkv")]
    [InlineData("\\\\host\\share\\x.mkv")]
    [InlineData("sources/file.mkv:stream")]
    [InlineData("sources/../../project2/file.mkv")]
    [InlineData("sources//file.mkv")]
    public async Task EscapingPathsAreRejected(string path)
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "safe");
        Assert.Throws<InvalidDataException>(() => ProjectPathPolicy.OpenSource(h, path));
    }

    [ProjectUnixFact]
    public async Task SourceReadRejectsLinkedAncestorAndLeaf()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "safe");
        var outside = Path.Combine(_root, "sentinel.mkv");
        await File.WriteAllTextAsync(outside, "unchanged");
        File.CreateSymbolicLink(Path.Combine(h.ProjectDirectory, "sources", "linked.mkv"), outside);
        Assert.ThrowsAny<IOException>(() => ProjectPathPolicy.OpenSource(h, "sources/linked.mkv"));
        Directory.CreateSymbolicLink(Path.Combine(h.ProjectDirectory, "sources", "external"), _root);
        Assert.ThrowsAny<IOException>(() => ProjectPathPolicy.OpenSource(h, "sources/external/sentinel.mkv"));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(outside));
    }

    [ProjectUnixFact]
    public async Task ReplacingOpenProjectPathCannotRedirectSave()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "safe");
        var original = h.ProjectDirectory;
        var moved = original + "-moved";
        Directory.Move(original, moved);
        Directory.CreateDirectory(original);
        await File.WriteAllTextAsync(Path.Combine(original, "project.opencam"), "sentinel");
        await h.SaveAsync(h.Current with { Revision = 1, Name = "actual" }, 0);
        Assert.Equal("sentinel", await File.ReadAllTextAsync(Path.Combine(original, "project.opencam")));
        Assert.Contains("actual", await File.ReadAllTextAsync(Path.Combine(moved, "project.opencam")));
    }

    [Fact]
    public async Task HardlinkedSourceIsRejectedWithoutChangingExternalFile()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "safe");
        var outside = Path.Combine(_root, "sentinel.mkv");
        await File.WriteAllTextAsync(outside, "unchanged");
        var linked = Path.Combine(h.ProjectDirectory, "sources", "linked.mkv");
        Assert.True(OperatingSystem.IsWindows() ? CreateHardLink(linked, outside, IntPtr.Zero) : link(outside, linked) == 0);
        Assert.Throws<InvalidDataException>(() => ProjectPathPolicy.OpenSource(h, "sources/linked.mkv"));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(outside));
    }

    [ProjectUnixFact]
    public async Task ManifestAndLockLinksCannotOverwriteExternalSentinel()
    {
        var store = new JsonProjectStore();
        string dir;
        await using (var h = await store.CreateAsync(_root, "safe")) dir = h.ProjectDirectory;
        var sentinel = Path.Combine(_root, "sentinel");
        await File.WriteAllTextAsync(sentinel, "do not touch");
        File.Delete(Path.Combine(dir, ".project.lock"));
        File.CreateSymbolicLink(Path.Combine(dir, ".project.lock"), sentinel);
        await Assert.ThrowsAnyAsync<IOException>(() => store.OpenAsync(Path.Combine(dir, "project.opencam")));
        Assert.Equal("do not touch", await File.ReadAllTextAsync(sentinel));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string destination);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string destination, string existing, IntPtr reserved);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal sealed class ProjectUnixFactAttribute : FactAttribute
{
    public ProjectUnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires Unix directory replacement/symlink behavior; Windows uses pinned handles.";
    }
}
