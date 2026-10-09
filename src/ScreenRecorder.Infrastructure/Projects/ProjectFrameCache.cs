// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Single-worker, rebuildable frame cache. All mutations are relative to a pinned cache directory.</summary>
public sealed class ProjectFrameCache : IDisposable
{
    private readonly BoundDirectory parent, directory;
    private readonly long quota;
    private readonly Dictionary<string, long> recent = new(StringComparer.Ordinal);
    private long clock = DateTime.UtcNow.Ticks;
    public ProjectFrameCache(IProjectHandle owner, long quotaBytes = 2L * 1024 * 1024 * 1024)
    {
        if (quotaBytes <= 0) throw new ArgumentOutOfRangeException(nameof(quotaBytes));
        quota = quotaBytes;
        parent = JsonProjectStore.Handle(owner).Root.OpenChild("cache", create: true);
        try { directory = parent.OpenChild("frames-v1", create: true); }
        catch { parent.Dispose(); throw; }
    }

    private static string Leaf(string key)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid media cache key.");
        return key.ToLowerInvariant() + ".rgba";
    }

    public async Task<byte[]?> ReadAsync(string key, int length, CancellationToken ct)
    {
        var leaf = Leaf(key);
        if (length <= 0 || length > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(length));
        try
        {
            using var stream = directory.Read(leaf);
            if (stream.Length != length + 32L) return null;
            var hash = new byte[32]; var bytes = new byte[length];
            await stream.ReadExactlyAsync(hash, ct);
            await stream.ReadExactlyAsync(bytes, ct);
            if (!CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(bytes))) return null;
            recent[leaf] = ++clock;
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
    }

    public async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        var leaf = Leaf(key);
        if (bytes.Length == 0 || bytes.Length > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        ct.ThrowIfCancellationRequested();
        if (bytes.LongLength + 32 > quota) return;
        if (await ReadAsync(key, bytes.Length, ct) is not null) return;
        if (new DriveInfo(Path.GetPathRoot(directory.CurrentPath)!).AvailableFreeSpace < 64L * 1024 * 1024)
            return; // Cache is optional; never compete with recording when storage is low.
        directory.DeleteOwnedFile(leaf); // Only this algorithm's derived image; never a source.
        var entries = new DirectoryInfo(directory.CurrentPath).EnumerateFiles("*.rgba")
            .Where(f => f.Name.Length == 69 && f.Name[..64].All(char.IsAsciiHexDigit) &&
                (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(f => (Name: f.Name, Length: f.Length, Last: recent.GetValueOrDefault(f.Name, f.LastWriteTimeUtc.Ticks)))
            .OrderBy(f => f.Last).ToArray();
        long total = entries.Sum(f => f.Length);
        foreach (var entry in entries)
        {
            if (total + bytes.LongLength + 32 <= quota) break;
            directory.DeleteOwnedFile(entry.Name);
            recent.Remove(entry.Name); total -= entry.Length;
        }
        var temp = "frame-" + Guid.NewGuid().ToString("N") + ".tmp";
        var published = false;
        try
        {
            await using (var stream = directory.CreateNew(temp))
            {
                await stream.WriteAsync(SHA256.HashData(bytes), ct);
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            directory.PublishVerified(temp, leaf);
            published = true; recent[leaf] = ++clock;
        }
        finally { if (!published) directory.DeleteOwnedFile(temp); }
    }
    public void Dispose() { directory.Dispose(); parent.Dispose(); }
}
