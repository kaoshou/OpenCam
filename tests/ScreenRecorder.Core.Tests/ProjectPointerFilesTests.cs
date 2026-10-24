// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using System.Security.Cryptography;
using System.Text;

namespace ScreenRecorder.Core.Tests;
public sealed class ProjectPointerFilesTests : IDisposable
{
    private readonly string _root=Directory.CreateTempSubdirectory("OpenCam-pointer-publish-").FullName;
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PromotesOnlyClockVerifiedTrackAndSurvivesRetry(bool valid)
    {
        await using var owner=await new JsonProjectStore().CreateAsync(_root,"project");
        var relative="sessions/Sessions/s/segment_000.mkv";
        var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{owner.Current.ProjectId}/{relative}"))[..16]);
        var source=new ProjectSource { Id=id,RelativePath=relative,SessionId="s",FileSize=100,Sha256=new string('a',64),
            Width=64,Height=64,VideoCodec="h264",Timing=new(new(1,1000),0,1000) };
        using var root=BoundDirectory.Open(owner.ProjectDirectory);
        using var sessions=root.OpenChild("sessions");
        using var catalog=sessions.OpenChild("Sessions",true);
        using var dir=catalog.OpenChild("s",true);
        using(var writer=PointerTrackWriter.Create(dir,"segment_000.mkv.pointer",new(owner.Current.ProjectId,id,64,64)))
        {
            writer.Append(new(new PointerSample(100,2,3,true,0),null));
            writer.Append(new(null,new NativeFrameClock(0,100,100,1)));
            writer.Seal(new(100,source.Sha256));
        }
        Task<long> Verify(PointerTrackReader reader)
        {
            while(reader.ReadNext()!=null) { }
            Assert.True(reader.Complete);
            return valid ? Task.FromResult(21_000L) : Task.FromException<long>(new InvalidDataException("clock mismatch"));
        }
        var result=await ProjectPointerFiles.PromoteAsync(owner,source,Verify);
        Assert.Equal(valid,result.Complete);
        Assert.Equal(valid ? 21_000 : 0,result.VideoPtsOffsetMicroseconds);
        Assert.Equal($"pointers/{id:N}.ocpt",result.RelativePath);
        Assert.Equal(result,await ProjectPointerFiles.PromoteAsync(owner,source,Verify));
        using var pointers=root.OpenChild("pointers");
        using var read=PointerTrackReader.Open(pointers,$"{id:N}.ocpt",new(owner.Current.ProjectId,id,64,64),
            new(100,source.Sha256,result.VideoPtsOffsetMicroseconds),!valid);
        while(read.ReadNext()!=null) { }
        Assert.Equal(valid,read.Complete);
    }
    [Fact]
    public async Task RefusesTrackReplacedBetweenClockVerificationAndPromotion()
    {
        await using var owner=await new JsonProjectStore().CreateAsync(_root,"project");
        var source=new ProjectSource { Id=Guid.NewGuid(),RelativePath="sources/video.mkv",SessionId="s",FileSize=100,
            Sha256=new string('a',64),Width=64,Height=64,VideoCodec="h264",Timing=new(new(1,1000),0,1000) };
        using var root=BoundDirectory.Open(owner.ProjectDirectory);
        using var dir=root.OpenChild("sources");
        void Write(string leaf,long pts)
        {
            using var writer=PointerTrackWriter.Create(dir,leaf,new(owner.Current.ProjectId,source.Id,64,64));
            writer.Append(new(new PointerSample(100,2,3,true,0),null));
            writer.Append(new(null,new NativeFrameClock(pts,100,100,1)));
            writer.Seal(new(100,source.Sha256));
        }
        Write("video.mkv.pointer",0);
        await Assert.ThrowsAsync<InvalidDataException>(()=>ProjectPointerFiles.PromoteAsync(owner,source,reader=>
        {
            while(reader.ReadNext()!=null) { }
            reader.Dispose(); // Simulate replacement in the post-verification reopen window, not a locked Windows handle.
            Write("replacement",1);
            File.Move(Path.Combine(owner.ProjectDirectory,"sources","replacement"),
                Path.Combine(owner.ProjectDirectory,"sources","video.mkv.pointer"),true);
            return Task.FromResult(0L);
        }));
        Assert.Empty(Directory.GetFiles(Path.Combine(owner.ProjectDirectory,"pointers")));
    }
    [Fact]
    public async Task MissingTrackIsExplicitlyIncompleteNotLegacy()
    {
        await using var owner=await new JsonProjectStore().CreateAsync(_root,"project");
        var source=new ProjectSource { Id=Guid.NewGuid(),RelativePath="sources/missing.mkv",SessionId="s",FileSize=100,
            Sha256=new string('a',64),Width=64,Height=64,VideoCodec="h264",Timing=new(new(1,1000),0,1000) };
        var result=await ProjectPointerFiles.PromoteAsync(owner,source,_=>throw new Exception("Must not verify missing input"));
        Assert.False(result.Complete); Assert.Equal(97,result.FileSize);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClockDeadlineRetainsIncompleteTrackButUserCancellationPropagates(bool userCanceled)
    {
        await using var owner=await new JsonProjectStore().CreateAsync(_root,"project");
        var source=new ProjectSource { Id=Guid.NewGuid(),RelativePath="sources/video.mkv",SessionId="s",FileSize=100,
            Sha256=new string('a',64),Width=64,Height=64,VideoCodec="h264",Timing=new(new(1,1000),0,1000) };
        using var root=BoundDirectory.Open(owner.ProjectDirectory);
        using var dir=root.OpenChild("sources");
        using(var writer=PointerTrackWriter.Create(dir,"video.mkv.pointer",new(owner.Current.ProjectId,source.Id,64,64)))
        {
            writer.Append(new(new PointerSample(100,2,3,true,0),null));
            writer.Append(new(null,new NativeFrameClock(0,100,100,1)));
            writer.Seal(new(100,source.Sha256));
        }
        using var cancellation=new CancellationTokenSource();
        Task<long> Verify(PointerTrackReader _)
        {
            if(userCanceled) cancellation.Cancel();
            return Task.FromException<long>(new OperationCanceledException("probe deadline"));
        }
        if(userCanceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ProjectPointerFiles.PromoteAsync(owner,source,Verify,cancellation.Token));
        else
        {
            var result=await ProjectPointerFiles.PromoteAsync(owner,source,Verify,cancellation.Token);
            Assert.False(result.Complete);
            Assert.Equal(97,result.FileSize);
            Assert.Equal(0,result.VideoPtsOffsetMicroseconds);
        }
    }
    public void Dispose()=>Directory.Delete(_root,true);
}
