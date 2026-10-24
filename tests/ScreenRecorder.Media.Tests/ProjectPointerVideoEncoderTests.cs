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
    public async Task CancelledWorkDoesNotConsumeProducerOrCloseOutput()
    {
        using var output=new MemoryStream();
        var called=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ProjectPointerVideoEncoder().EncodeAsync(
            new(64,64,new(10,1)),18,1,output,(input,ct)=>{called=true;return Task.CompletedTask;},new(true)));
        Assert.False(called); Assert.True(output.CanWrite);
    }
}
