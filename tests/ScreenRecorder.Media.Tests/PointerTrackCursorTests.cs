// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class PointerTrackCursorTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "opencam-pointer-cursor-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task RationalMediaPtsUsesVerifiedMuxOffsetAndNativeFrameAnchor()
    {
        var (owner, source) = await CreateAsync(new(1, 90000), 1890);
        await using var project = owner;
        using var cursor = await PointerTrackCursor.OpenAsync(project, source);
        Assert.Equal(new PointerSample(8_000_000, 10, 20, true, 0), await cursor.ResolveAsync(1890));
        Assert.Equal(new PointerSample(8_025_000, 60, 70, true, 0), await cursor.ResolveAsync(4140));
        Assert.Equal(new PointerSample(8_050_000, 110, 120, true, 1), await cursor.ResolveAsync(6390));
    }

    [Fact]
    public async Task VisibilityBoundariesLongGapsAndEndNeverExtrapolate()
    {
        var (owner, source) = await CreateAsync(new(1,1000), 21);
        await using var project = owner;
        using var cursor = await PointerTrackCursor.OpenAsync(project, source);
        foreach (var pts in new long[] { 20, 76, 81, 86, 121, 322 })
            Assert.Null(await cursor.ResolveAsync(pts));
        Assert.Equal(new PointerSample(8_070_000, 210, 220, true, 0), await cursor.ResolveAsync(91));
    }

    [Fact]
    public async Task BackwardSeekReopensOwnedStreamAndRepeatedLookupRetainsInterpolation()
    {
        var (owner, source) = await CreateAsync(new(1,1000), 21);
        await using var project = owner;
        using var cursor = await PointerTrackCursor.OpenAsync(project, source);
        Assert.Null(await cursor.ResolveAsync(322));
        Assert.Equal(60, (await cursor.ResolveAsync(46))!.Value.X);
        Assert.Equal(60, (await cursor.ResolveAsync(46))!.Value.X);
        Assert.Equal(110, (await cursor.ResolveAsync(71))!.Value.X);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task EntireTrackAndSourceSealAreVerifiedBeforeFirstPointCanEscape(bool seal, bool wrongSeal)
    {
        // The initial point is valid and the manifest fingerprint matches the
        // whole file. Only consuming the tail detects these incomplete sources.
        var (owner, source) = await CreateAsync(new(1,1000), 21, seal, wrongSeal);
        await using var project = owner;
        await Assert.ThrowsAsync<InvalidDataException>(() => PointerTrackCursor.OpenAsync(project, source));
    }

    [Fact]
    public async Task ForeignSourceCancellationAndDisposedCursorFailWithoutPublishingData()
    {
        var (owner, source) = await CreateAsync(new(1,1000), 21);
        await using var project = owner;
        await Assert.ThrowsAsync<InvalidDataException>(() => PointerTrackCursor.OpenAsync(project,
            source with { Id = Guid.NewGuid() }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PointerTrackCursor.OpenAsync(project, source, cancelled.Token));
        var cursor = await PointerTrackCursor.OpenAsync(project, source);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cursor.ResolveAsync(21, cancelled.Token));
        Assert.Equal(10, (await cursor.ResolveAsync(21))!.Value.X);
        cursor.Dispose(); cursor.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await cursor.ResolveAsync(21));
    }

    [Fact]
    public async Task ExtremePtsCannotWrapIntoAnUnrelatedValidPointerPosition()
    {
        var (owner, source) = await CreateAsync(new(1001,90000), 0);
        await using var project = owner;
        using var cursor = await PointerTrackCursor.OpenAsync(project, source);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await cursor.ResolveAsync(long.MaxValue));
    }

    private async Task<(IProjectHandle, ProjectSource)> CreateAsync(ProjectRational timeBase, long startPts,
        bool seal = true, bool wrongSeal = false)
    {
        var owner = await new JsonProjectStore().CreateAsync(_root, "cursor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = Guid.NewGuid();
            var identity = new PointerTrackSource(100, new string('a',64), 21_000);
            using var root = BoundDirectory.Open(owner.ProjectDirectory);
            using var folder = root.OpenChild("pointers", create: true);
            using (var writer = PointerTrackWriter.Create(folder, $"{id:N}.ocpt", new(owner.Current.ProjectId, id, 640, 480)))
            {
                writer.Append(new(new PointerSample(8_000_000,10,20,true,0),null));
                writer.Append(new(null,new NativeFrameClock(0,8_000_000,7_999_000,1)));
                writer.Append(new(new PointerSample(8_050_000,110,120,true,1),null));
                writer.Append(new(new PointerSample(8_060_000,0,0,false,0),null));
                writer.Append(new(new PointerSample(8_070_000,210,220,true,0),null));
                writer.Append(new(new PointerSample(8_300_001,310,320,true,0),null));
                writer.Append(new(null,new NativeFrameClock(333_000,8_333_000,8_332_000,2)));
                if (seal) writer.Seal(wrongSeal ? identity with { FileSize = 101 } : identity);
            }
            long length; string digest;
            using (var stream = folder.Read($"{id:N}.ocpt"))
            { length = stream.Length; digest = Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
            var source = new ProjectSource { Id=id, SessionId="s", RelativePath="sources/video.mkv",
                FileSize=identity.FileSize, Sha256=identity.Sha256, Width=640, Height=480, VideoCodec="h264",
                Timing=new(timeBase,startPts,90_000), Pointer=new($"pointers/{id:N}.ocpt",length,digest,21_000,true) };
            await owner.SaveAsync(owner.Current with { SchemaVersion=5, Revision=1, Sources=[source], Sessions=["s"] },0);
            return (owner,source);
        }
        catch { await owner.DisposeAsync(); throw; }
    }

    public void Dispose() => Directory.Delete(_root,true);
}
