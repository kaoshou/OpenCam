// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectFrameCacheTests
{
    [UnixOnlyFact]
    public async Task ReplacedCacheDirectoryCannotRedirectWrites()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-cache-pinned-");
        try
        {
            await using var project = await new JsonProjectStore().CreateAsync(root.FullName, "cache");
            using var cache = new ProjectFrameCache(project);
            var path = Path.Combine(project.ProjectDirectory, "cache", "frames-v1");
            Directory.Move(path, path + ".original");
            Directory.CreateDirectory(path);
            var key = new string('b', 64);
            var sentinel = Path.Combine(path, key + ".rgba");
            await File.WriteAllTextAsync(sentinel, "replacement");
            await cache.PutAsync(key, new byte[] { 1, 2, 3, 255 }, default);
            Assert.Equal("replacement", await File.ReadAllTextAsync(sentinel));
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, await cache.ReadAsync(key, 4, default));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task BoundedCacheEvictsLeastRecentlyUsedAndNeverTouchesSources()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-cache-");
        try
        {
            await using var project = await new JsonProjectStore().CreateAsync(root.FullName, "cache");
            var source = Path.Combine(project.ProjectDirectory, "sources", "keep.mkv");
            await File.WriteAllTextAsync(source, "original");
            using var cache = new ProjectFrameCache(project, 72); // Two four-byte images plus checksum headers.
            var a = new string('a', 64); var b = new string('b', 64); var c = new string('c', 64);
            await cache.PutAsync(a, new byte[] { 1, 2, 3, 255 }, default);
            await cache.PutAsync(b, new byte[] { 4, 5, 6, 255 }, default);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, await cache.ReadAsync(a, 4, default));
            await cache.PutAsync(c, new byte[] { 7, 8, 9, 255 }, default);
            Assert.Null(await cache.ReadAsync(b, 4, default));
            Assert.NotNull(await cache.ReadAsync(a, 4, default));
            Assert.NotNull(await cache.ReadAsync(c, 4, default));
            Assert.Equal("original", await File.ReadAllTextAsync(source));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task CorruptOrWrongSizedCacheIsAMissAndTraversalIsRejected()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-cache-invalid-");
        try
        {
            await using var project = await new JsonProjectStore().CreateAsync(root.FullName, "cache");
            using var cache = new ProjectFrameCache(project);
            var key = new string('a', 64);
            await cache.PutAsync(key, new byte[] { 1, 2, 3, 255 }, default);
            Assert.Null(await cache.ReadAsync(key, 8, default));
            await File.WriteAllBytesAsync(Path.Combine(project.ProjectDirectory, "cache", "frames-v1", key + ".rgba"), new byte[36]);
            Assert.Null(await cache.ReadAsync(key, 4, default));
            await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReadAsync("../sources/keep", 4, default));
        }
        finally { root.Delete(true); }
    }
}
