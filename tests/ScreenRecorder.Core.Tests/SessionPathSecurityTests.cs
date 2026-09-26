// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Core.Tests;

public sealed class SessionPathSecurityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-path-tests-").FullName;

    [Fact]
    public async Task LoadedDirectoryIsBoundToTheDirectoryActuallyOpened()
    {
        var dir = Path.Combine(_root, "session");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), JsonSerializer.Serialize(new RecordingSession
        {
            SessionId = "legacy-session", WorkingDirectory = Path.Combine(_root, "external")
        }));
        var session = await new JsonRecordingSessionStore().LoadSessionAsync(dir);
        Assert.NotNull(session);
        Assert.Equal(dir, session.WorkingDirectory);
        Assert.Equal("legacy-session", session.SessionId);
    }

    [Theory]
    [InlineData("../outside.mkv")]
    [InlineData("child/segment.mkv")]
    [InlineData("segment.mkv\nfile 'outside'")]
    [InlineData("not-media.txt")]
    public void ExternalAndParserControlPathsAreRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => SessionPathPolicy.SegmentPath(_root, path));
    }

    [Theory]
    [InlineData("session.json")]
    [InlineData("session.json.bak")]
    [InlineData("session.json.tmp")]
    public async Task SaveNeverOverwritesLinkedMetadata(string leaf)
    {
        var dir = Path.Combine(_root, "session");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(_root, "do-not-change.txt");
        await File.WriteAllTextAsync(target, "preserve");
        File.CreateSymbolicLink(Path.Combine(dir, leaf), target);
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonRecordingSessionStore().SaveSessionAsync(
            new RecordingSession { SessionId = "session", WorkingDirectory = dir }));
        Assert.Equal("preserve", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task PrimaryAndBackupCannotImportExternalSegments()
    {
        var dir = Path.Combine(_root, "session");
        Directory.CreateDirectory(dir);
        var outside = Path.Combine(_root, "outside.mkv");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        var json = JsonSerializer.Serialize(new RecordingSession
        {
            SessionId = "session", WorkingDirectory = dir, WorkingFilePath = outside,
            SegmentFilePaths = [outside]
        });
        await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);
        await File.WriteAllTextAsync(Path.Combine(dir, "session.json.bak"), json);
        Assert.Null(await new JsonRecordingSessionStore().LoadSessionAsync(dir));
    }

    [Fact]
    public void LinkedSegmentCannotLeaveSession()
    {
        var link = Path.Combine(_root, "segment_0001.mkv");
        File.CreateSymbolicLink(link, Path.Combine(_root, "missing-target.mkv"));
        Assert.Throws<InvalidDataException>(() => SessionPathPolicy.SegmentPath(_root, link));
    }

    [Theory]
    [InlineData("session.json")]
    [InlineData("session.json.bak")]
    [InlineData("session.json.tmp")]
    public async Task SavingMetadataDoesNotTruncateHardLinkedExternalFiles(string leaf)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "session")).FullName;
        var target = Path.Combine(_root, "sentinel.txt");
        await File.WriteAllTextAsync(target, "preserve");
        var destination = Path.Combine(dir, leaf);
        Assert.True(OperatingSystem.IsWindows()
            ? CreateHardLink(destination, target, IntPtr.Zero)
            : link(target, destination) == 0);
        var save = new JsonRecordingSessionStore().SaveSessionAsync(new RecordingSession
        {
            SessionId = "session", WorkingDirectory = dir
        });
        if (leaf == "session.json") await Assert.ThrowsAsync<InvalidDataException>(() => save);
        else await save;
        Assert.Equal("preserve", await File.ReadAllTextAsync(target));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string path);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
