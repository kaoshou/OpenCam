// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;
public sealed class ProjectPointerVideoEncoderTests
{
    [Fact]
    public async Task EncodesStreamingFramesWithoutWholeVideoBuffer()
    {
        var dir=Directory.CreateTempSubdirectory("OpenCam-pointer-encode-");
        try
        {
            var path=Path.Combine(dir.FullName,"video.h264");
            await using(var output=new FileStream(path,FileMode.CreateNew))
                await new ProjectPointerVideoEncoder().EncodeAsync(new(64,64,new(10,1)),18,3,output,
                    async (input,ct)=> { for(var i=0;i<3;i++) await input.WriteAsync(new byte[64*64*4],ct); });
            var probe=await new MediaFileProbe().ProbeAsync(path);
            Assert.True(probe.IsValid,probe.RawJsonOutput);
            Assert.Equal(1,probe.VideoStreamCount);
            Assert.Equal(0,probe.AudioStreamCount);
            Assert.True(new FileInfo(path).Length>0);
        }
        finally { dir.Delete(true); }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(64*64*4-1)]
    [InlineData(64*64*4+1)]
    public async Task RejectsShortOrOverlongProducer(int length)
    {
        using var output=new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(()=>new ProjectPointerVideoEncoder().EncodeAsync(
            new(64,64,new(10,1)),18,1,output,async(input,ct)=>await input.WriteAsync(new byte[length],ct)));
        Assert.True(output.CanWrite);
    }
    [Fact]
    public async Task OutputFailureCancelsAndJoinsProducerWithoutCallerCancellation()
    {
        using var output=new FailingOutput();
        using var cleanup=new CancellationTokenSource();
        var producerStopped=false;
        var work=new ProjectPointerVideoEncoder().EncodeAsync(new(512,512,new(30,1)),18,10000,output,
            async(input,ct)=>
            {
                var frame=new byte[512*512*4];
                new Random(42).NextBytes(frame);
                try { for(var i=0;i<10000;i++) await input.WriteAsync(frame,ct); }
                finally { producerStopped=true; }
            },cleanup.Token);
        try
        {
            var failure=await Assert.ThrowsAsync<IOException>(()=>work.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("Simulated full disk",failure.Message);
            Assert.True(producerStopped);
            Assert.True(output.CanWrite);
        }
        finally
        {
            cleanup.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(5)); } catch(Exception) when(work.IsCompleted) { }
        }
    }
    private sealed class FailingOutput:MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)
            =>ValueTask.FromException(new IOException("Simulated full disk"));
    }
    [Fact]
    public async Task CancelledWorkDoesNotConsumeProducerOrCloseOutput()
    {
        using var output=new MemoryStream();
        var called=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ProjectPointerVideoEncoder().EncodeAsync(
            new(64,64,new(10,1)),18,1,output,(input,ct)=>{called=true;return Task.CompletedTask;},new(true)));
        Assert.False(called); Assert.True(output.CanWrite);
    }
}
