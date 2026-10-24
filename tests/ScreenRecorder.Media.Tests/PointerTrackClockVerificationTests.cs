// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class PointerTrackClockVerificationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-pointer-clock-").FullName;
    private readonly PointerTrackHeader _header = new(Guid.NewGuid(), Guid.NewGuid(), 64, 64);
    private readonly PointerTrackSource _source = new(100, new string('a',64));

    private PointerTrackReader Track(bool seal = true)
    {
        using var folder = BoundDirectory.Open(_root);
        using (var writer = PointerTrackWriter.Create(folder, "track", _header))
        {
            writer.Append(new(new PointerSample(8_000_000,10,20,true,0),null));
            writer.Append(new(null,new NativeFrameClock(0,8_000_000,8_000_000,1)));
            writer.Append(new(new PointerSample(8_050_000,20,20,true,0),null));
            writer.Append(new(null,new NativeFrameClock(100_000,8_100_000,8_100_000,2)));
            if (seal) writer.Seal(_source);
        }
        return PointerTrackReader.Open(folder,"track",_header,_source);
    }
    private static async IAsyncEnumerable<long> Frames(params long[] values)
    { foreach(var value in values) { await Task.Yield(); yield return value; } }

    [Fact]
    public async Task AcceptsVerifiedConstantMuxOffsetAndConsumesSeal()
    {
        using var reader = Track();
        Assert.Equal(21_000, await PointerTrackClockVerification.VerifyAsync(reader,Frames(21_000,121_000)));
        Assert.True(reader.Complete);
    }
    [Theory]
    [InlineData(21_000)]
    [InlineData(21_000,121_000,221_000)]
    [InlineData(21_000,125_000)]
    public async Task RejectsMissingExtraOrRetimedVideoFrames(params long[] frames)
    {
        using var reader = Track();
        await Assert.ThrowsAsync<InvalidDataException>(()=>PointerTrackClockVerification.VerifyAsync(reader,Frames(frames)));
    }
    [Fact]
    public async Task MatchingFramesWithoutCleanStopSealAreNotComplete()
    {
        using var reader = Track(false);
        await Assert.ThrowsAsync<InvalidDataException>(()=>PointerTrackClockVerification.VerifyAsync(reader,Frames(0,100_000)));
    }
    public void Dispose()=>Directory.Delete(_root,true);
}
