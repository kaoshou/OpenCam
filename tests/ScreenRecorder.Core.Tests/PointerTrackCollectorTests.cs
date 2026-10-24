// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Core.Tests;
public sealed class PointerTrackCollectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-pointer-collector-").FullName;
    private readonly PointerTrackHeader _header = new(Guid.NewGuid(),Guid.NewGuid(),64,64);
    private readonly PointerTrackSource _source = new(100,new string('a',64));
    [Fact]
    public async Task DrainsAcceptedEventsBeforeCleanSeal()
    {
        using var dir=BoundDirectory.Open(_root);
        await using var collector=new PointerTrackCollector(PointerTrackWriter.Create(dir,"track",_header));
        Assert.True(collector.TryAppend(new(new PointerSample(100,2,3,true,0),null)));
        Assert.True(collector.TryAppend(new(null,new NativeFrameClock(0,100,100,1))));
        Assert.True(await collector.CompleteAsync(_source));
        Assert.False(collector.TryAppend(new(new PointerSample(101,4,5,true,0),null)));
        await collector.DisposeAsync(); // Production releases the write handle before opening the sealed track.
        using var read=PointerTrackReader.Open(dir,"track",_header,_source);
        Assert.Equal(2,read.ReadNext()!.Value.Pointer!.Value.X);
        Assert.NotNull(read.ReadNext()!.Value.Frame);
        Assert.Null(read.ReadNext()); Assert.True(read.Complete);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidProducerOrExplicitFailureCannotSeal(bool invalid)
    {
        using var dir=BoundDirectory.Open(_root);
        await using var collector=new PointerTrackCollector(PointerTrackWriter.Create(dir,"track",_header));
        collector.TryAppend(new(new PointerSample(100,2,3,true,0),null));
        collector.TryAppend(new(null,new NativeFrameClock(0,100,100,1)));
        if(invalid) collector.TryAppend(new(new PointerSample(99,2,3,true,0),null));
        else collector.MarkIncomplete();
        Assert.False(await collector.CompleteAsync(_source));
        await collector.DisposeAsync();
        using var read=PointerTrackReader.Open(dir,"track",_header,_source,true);
        while(read.ReadNext()!=null) { }
        Assert.False(read.Complete);
    }
    public void Dispose()=>Directory.Delete(_root,true);
}
