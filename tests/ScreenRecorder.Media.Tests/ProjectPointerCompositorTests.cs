// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPointerCompositorTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pointer-composite-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task TrimmedFrameAndBackwardSeekDrawAtRecordedSourceHotspot()
    {
        await using var owner = await CreateAsync();
        var project = owner.Current;
        var plan = ProjectRenderPlan.Create(project);
        using var compositor = new ProjectPointerCompositor(plan, (source, ct) => PointerTrackCursor.OpenAsync(owner,source,ct));
        foreach (var (frame,x) in new[] { (0L,40), (1L,60), (0L,40) })
        {
            var pixels = new byte[320*180*4];
            await compositor.ApplyAsync(pixels,frame,320,180);
            Assert.True(pixels[(60*320+x)*4+3]>0);
            Assert.Equal(0,pixels[(120*320+200)*4+3]);
        }
    }

    [Fact]
    public async Task HiddenModeNeverRequiresMissingTrackAndOutOfRangeDoesNotDraw()
    {
        await using var owner = await CreateAsync();
        var project=owner.Current with { PointerSettings=new(ProjectPointerStyle.Hidden) };
        using var compositor=new ProjectPointerCompositor(ProjectRenderPlan.Create(project), (_,_)=>throw new InvalidOperationException("Must not open"));
        var pixels=new byte[320*180*4];
        await compositor.ApplyAsync(pixels,0,320,180);
        Assert.All(pixels,b=>Assert.Equal(0,b));
        Assert.False(ProjectPointerCompositor.IsRequired(ProjectRenderPlan.Create(project)));
    }

    [Fact]
    public async Task DamagedPointerDoesNotInterruptOriginalPreviewButExportStillRejectsIt()
    {
        await using var owner = await CreateAsync();
        var source = owner.Current.Sources[0];
        await File.AppendAllTextAsync(Path.Combine(owner.ProjectDirectory, source.Pointer!.RelativePath), "tamper");
        using var compositor = new ProjectPointerCompositor(ProjectRenderPlan.Create(owner.Current),
            (s, ct) => PointerTrackCursor.OpenAsync(owner, s, ct));
        var pixels = Enumerable.Repeat((byte)71, 320 * 180 * 4).ToArray();
        var original = pixels.ToArray();
        await compositor.ApplyAsync(pixels, 0, 320, 180, preview: true);
        Assert.Equal(original, pixels);
        Assert.True(compositor.HadUnavailablePointer);
        await compositor.ApplyAsync(pixels, 1, 320, 180, preview: true);
        Assert.Equal(original, pixels);
        using var export = new ProjectPointerCompositor(ProjectRenderPlan.Create(owner.Current),
            (s, ct) => PointerTrackCursor.OpenAsync(owner, s, ct));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await export.ApplyAsync(pixels, 0, 320, 180));
    }

    [Fact]
    public async Task DamagedTrackIsRejectedBeforeAnyPointerPixelsArePublished()
    {
        await using var owner=await CreateAsync();
        var source=owner.Current.Sources[0];
        await File.AppendAllTextAsync(Path.Combine(owner.ProjectDirectory,source.Pointer!.RelativePath),"tamper");
        using var compositor=new ProjectPointerCompositor(ProjectRenderPlan.Create(owner.Current),(s,ct)=>PointerTrackCursor.OpenAsync(owner,s,ct));
        var pixels=new byte[320*180*4];
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await compositor.ApplyAsync(pixels,0,320,180));
        Assert.All(pixels,b=>Assert.Equal(0,b));
    }

    private async Task<IProjectHandle> CreateAsync()
    {
        var owner=await new JsonProjectStore().CreateAsync(_root,"composite");
        var id=Guid.NewGuid();
        using var root=BoundDirectory.Open(owner.ProjectDirectory);
        using var folder=root.OpenChild("pointers",create:true);
        var identity=new PointerTrackSource(100,new string('a',64),0);
        using(var writer=PointerTrackWriter.Create(folder,$"{id:N}.ocpt",new(owner.Current.ProjectId,id,320,180)))
        {
            writer.Append(new(new PointerSample(1_000_000,20,60,true,0),null));
            writer.Append(new(null,new NativeFrameClock(0,1_000_000,999_000,1)));
            writer.Append(new(new PointerSample(1_050_000,40,60,true,0),null));
            writer.Append(new(new PointerSample(1_100_000,60,60,true,0),null));
            writer.Append(new(new PointerSample(1_150_000,80,60,true,0),null));
            writer.Seal(identity);
        }
        long length;string sha;
        using(var input=folder.Read($"{id:N}.ocpt")) { length=input.Length;sha=Convert.ToHexString(await SHA256.HashDataAsync(input)); }
        var source=new ProjectSource { Id=id,SessionId="s",RelativePath="sources/video.mkv",FileSize=100,Sha256=identity.Sha256,
            Width=320,Height=180,VideoCodec="h264",Timing=new(new(1,1000),0,200),Pointer=new($"pointers/{id:N}.ocpt",length,sha,0,true) };
        var clip=new ProjectClip { Id=Guid.NewGuid(),Name="clip",SourceId=id,InPts=50,OutPts=150 };
        await owner.SaveAsync(owner.Current with {SchemaVersion=5,Revision=1,Canvas=new(320,180,new(20,1)),Sources=[source],Sessions=["s"],Clips=[clip],PointerSettings=new(ProjectPointerStyle.Dot)},0);
        return owner;
    }

    public void Dispose()=>Directory.Delete(_root,true);
}
