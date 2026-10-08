// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Core.Tests;

public class BoundDirectoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-bound-tests-").FullName;

    [Fact]
    public async Task OpenedDirectoryReadStaysOnOriginalAfterAncestorReplacement()
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "original")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(original, "segment.mkv"), "original");
        await File.WriteAllTextAsync(Path.Combine(outside, "segment.mkv"), "external");
        using var bound = BoundDirectory.Open(original);
        try
        {
            Directory.Move(original, original + "-moved");
            Directory.CreateSymbolicLink(original, outside);
        }
        catch (IOException) when (OperatingSystem.IsWindows()) { }
        using var stream = bound.Read("segment.mkv");
        using var reader = new StreamReader(stream);
        Assert.Equal("original", await reader.ReadToEndAsync());
    }

    [Fact]
    public void OpeningLinkedAncestorIsRejected()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside", "session")).FullName;
        var link = Path.Combine(_root, "linked");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);
        Assert.Throws<IOException>(() => BoundDirectory.Open(Path.Combine(link, "session")));
    }

    [Fact]
    public async Task PublicationNeverOverwritesAnExistingFile()
    {
        using var bound = BoundDirectory.Open(_root);
        var staged = Path.Combine(_root, "staged.mp4");
        var final = Path.Combine(_root, "final.mp4");
        await File.WriteAllTextAsync(staged, "recovered");
        await File.WriteAllTextAsync(final, "preserve");
        await Assert.ThrowsAsync<IOException>(() => bound.PublishAsync(staged, "final.mp4", default));
        Assert.Equal("preserve", await File.ReadAllTextAsync(final));
        var output = await bound.PublishAsync(staged, "new.mp4", default);
        Assert.Equal("recovered", await File.ReadAllTextAsync(output));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(output));
    }

    [Fact]
    public void ConcurrentRecoveryClaimsAreRejected()
    {
        using var bound = BoundDirectory.Open(_root);
        using var first = bound.Claim();
        Assert.Throws<IOException>(() => bound.Claim());
    }

    [Fact]
    public async Task VerifiedPublicationMovesCompleteFileWithoutReplacingOldOutput()
    {
        using var bound = BoundDirectory.Open(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "work.tmp"), "verified-complete");
        await File.WriteAllTextAsync(Path.Combine(_root, "old.mp4"), "keep");
        Assert.Throws<IOException>(() => bound.PublishVerified("work.tmp", "old.mp4"));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, "old.mp4")));
        var result = bound.PublishVerified("work.tmp", "new.mp4");
        Assert.Equal("verified-complete", await File.ReadAllTextAsync(result));
        Assert.False(File.Exists(Path.Combine(_root, "work.tmp")));
    }

    [Fact]
    public async Task VerifiedPublicationStaysInPinnedDirectoryAfterReplacement()
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(original, "work.tmp"), "owned");
        using var bound = BoundDirectory.Open(original);
        try {
            Directory.Move(original, original + "-moved");
            Directory.CreateSymbolicLink(original, outside);
        }
        catch (IOException) when (OperatingSystem.IsWindows()) { }
        var output = bound.PublishVerified("work.tmp", "result.mp4");
        Assert.Equal("owned", await File.ReadAllTextAsync(output));
        Assert.False(File.Exists(Path.Combine(outside, "result.mp4")));
    }

    [Fact]
    public async Task VerifiedPublicationRejectsInvalidEmptyAndLinkedSources()
    {
        using var bound = BoundDirectory.Open(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "empty.tmp"), "");
        await File.WriteAllTextAsync(Path.Combine(_root, "source.tmp"), "keep");
        File.CreateSymbolicLink(Path.Combine(_root, "linked.tmp"), Path.Combine(_root, "source.tmp"));
        Assert.Throws<InvalidDataException>(() => bound.PublishVerified("../source.tmp", "result.mp4"));
        Assert.Throws<InvalidDataException>(() => bound.PublishVerified("source.tmp", "source.tmp"));
        Assert.Throws<InvalidDataException>(() => bound.PublishVerified("empty.tmp", "result.mp4"));
        Assert.ThrowsAny<IOException>(() => bound.PublishVerified("linked.tmp", "result.mp4"));
        Assert.False(File.Exists(Path.Combine(_root, "result.mp4")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, "source.tmp")));
    }

    [Fact]
    public async Task ConcurrentVerifiedPublicationsHaveExactlyOneWinner()
    {
        using var bound = BoundDirectory.Open(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "first.tmp"), "first");
        await File.WriteAllTextAsync(Path.Combine(_root, "second.tmp"), "second");
        var results = await Task.WhenAll(new[] { "first", "second" }.Select(name => Task.Run(() => {
            try { bound.PublishVerified(name + ".tmp", "result.mp4"); return name; }
            catch (IOException) { return null; }
        })));
        var winner = Assert.Single(results.Where(r => r is not null));
        Assert.Equal(winner, await File.ReadAllTextAsync(Path.Combine(_root, "result.mp4")));
        Assert.False(File.Exists(Path.Combine(_root, winner + ".tmp")));
        var loser = winner == "first" ? "second" : "first";
        Assert.Equal(loser, await File.ReadAllTextAsync(Path.Combine(_root, loser + ".tmp")));
    }

    [Fact]
    public async Task LinkedRecoveryLockDoesNotTouchTarget()
    {
        var target = Path.Combine(_root, "sentinel");
        await File.WriteAllTextAsync(target, "preserve");
        File.CreateSymbolicLink(Path.Combine(_root, ".recovery.lock"), target);
        using var bound = BoundDirectory.Open(_root);
        Assert.Throws<IOException>(() => bound.Claim());
        Assert.Equal("preserve", await File.ReadAllTextAsync(target));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
