// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectGifExportIntegrationTests
{
    [MacOsOnlyFact]
    public async Task EveryTruncationAndTrailingGarbageOfRealGifIsRejected()
    {
        var root=Directory.CreateTempSubdirectory("OpenCam-gif-structure-");
        try
        {
            await using var owner=await RecordingContentExportIntegrationTests.SingleClip(root.FullName);
            var output=Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            var result=await new ProjectGifExporter(Process(),new()).ExportAsync(owner,owner.Current,output,Guid.NewGuid(),new InlineProgress(_=>{}),default);
            var bytes=await File.ReadAllBytesAsync(result.FinalPath!);
            for(var length=0;length<bytes.Length;length++)
                Assert.Throws<InvalidDataException>(()=>GifStructureReader.Read(new MemoryStream(bytes,0,length,false)));
            Assert.Throws<InvalidDataException>(()=>GifStructureReader.Read(new MemoryStream([..bytes,0])));
            bytes[0]=0;
            Assert.Throws<InvalidDataException>(()=>GifStructureReader.Read(new MemoryStream(bytes)));
        }
        finally {root.Delete(true);}
    }

    [MacOsOnlyTheory]
    [InlineData(.55)] [InlineData(.7)] [InlineData(.9)]
    public async Task CancellationAtEachStageLeavesOriginalAndExistingOutputUntouched(double stage)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-cancel-");
        try
        {
            await using var owner = await RecordingContentExportIntegrationTests.SingleClip(root.FullName, true);
            var output = Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            var old = Path.Combine(output,"existing.gif"); await File.WriteAllTextAsync(old,"keep");
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProjectGifExporter(Process(),new()).ExportAsync(
                owner,owner.Current,output,Guid.NewGuid(),new InlineProgress(value => { if(value >= stage) cancellation.Cancel(); }),cancellation.Token));
            Assert.Equal([old],Directory.GetFiles(output));
            Assert.Equal("keep",await File.ReadAllTextAsync(old));
            AssertSource(owner);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task ReplacedDirectoryCannotRedirectGifPublicationOrCleanup()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-swap-");
        try
        {
            await using var owner = await RecordingContentExportIntegrationTests.SingleClip(root.FullName);
            var output = Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            var moved = output + "-owned"; var swapped = false;
            var result = await new ProjectGifExporter(Process(),new()).ExportAsync(owner,owner.Current,output,Guid.NewGuid(),
                new InlineProgress(_ => { if(swapped) return; swapped=true; Directory.Move(output,moved);
                    Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"keep"),"replacement"); }),default);
            Assert.True(result.Success,result.Error);
            Assert.Equal(Path.GetFullPath(moved).Replace("/var/","/private/var/"),Path.GetDirectoryName(result.FinalPath));
            Assert.Single(Directory.GetFiles(moved)); Assert.Single(Directory.GetFiles(output));
            Assert.Equal("replacement",await File.ReadAllTextAsync(Path.Combine(output,"keep"))); AssertSource(owner);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task SelectedCompoundPreservesOrderEffectsAndMp4AudioButGifIsSilent()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-compound-");
        try
        {
            await using var owner = await new JsonProjectStore().CreateAsync(root.FullName,"fixture");
            var sourcePath = Path.Combine(owner.ProjectDirectory,"sources/colors.mkv");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await RecordingContentExportIntegrationTests.Run(ffmpeg,["-f","lavfi","-i","color=red:s=64x36:r=30:d=1",
                "-f","lavfi","-i","color=blue:s=64x36:r=30:d=1", "-f","lavfi","-i","sine=frequency=440:sample_rate=48000:duration=2",
                "-filter_complex","[0:v][1:v]concat=n=2:v=1:a=0[v]","-map","[v]","-map","2:a","-c:v","libx264","-bf","0","-c:a","pcm_s16le",sourcePath]);
            var sourceId=Guid.NewGuid();
            var project=owner.Current with { SchemaVersion=3,Revision=1,Canvas=new(64,36,new(30,1)),Sessions=["s"],
                Sources=[new() { Id=sourceId,SessionId="s",RelativePath="sources/colors.mkv",Width=64,Height=36,
                    VideoCodec="h264",AudioCodec="pcm_s16le",Timing=new(new(1,1000),0,2000),FileSize=new FileInfo(sourcePath).Length,
                    Sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) }],
                Clips=[new() { Id=Guid.NewGuid(),SourceId=sourceId,Name="blue",InPts=1200,OutPts=1500,Scale=1.25,PositionX=25,Crop=10,Volume=.5,FadeInTicks=500000 },
                    new() { Id=Guid.NewGuid(),SourceId=sourceId,Name="red",InPts=200,OutPts=500,Scale=1.25,PositionX=25,Crop=10,Muted=true }] };
            var merged=ProjectClipProjection.Combine(project,project.Clips[0],project.Clips[1]);
            var group=Guid.NewGuid();
            project=project with { Clips=[project.Clips[0] with {Id=Guid.NewGuid(),GroupId=group},merged with {GroupId=group}] };
            await owner.SaveAsync(project,0);
            var selected=ProjectExportSelection.Create(owner.Current,new(Scope:ProjectExportScope.Clip,ClipId:merged.Id));
            Assert.Equal(6_000_000,ProjectTimeline.Build(selected).DurationTicks);
            var output=Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            var gif=await new ProjectGifExporter(Process(),new()).ExportAsync(owner,selected,output,Guid.NewGuid(),new InlineProgress(_=>{}),default);
            var mp4=await new ProjectFfmpegExporter(Process()).ExportAsync(owner,selected,output,Guid.NewGuid(),new InlineProgress(_=>{}),default);
            Assert.True(gif.Success,gif.Error); Assert.True(mp4.Success,mp4.Error);
            using(var input=File.OpenRead(gif.FinalPath!))
            {
                var info=GifStructureReader.Read(input); Assert.Equal(9,info.FrameCount); Assert.Equal(60,info.DurationCentiseconds); Assert.True(info.InfiniteLoop);
                var inspection=await Process().RunAsync(ProjectMediaJob.InspectGif(),[input],null,default);
                using var json=System.Text.Json.JsonDocument.Parse(inspection.Output);
                Assert.Equal("video",Assert.Single(json.RootElement.GetProperty("streams").EnumerateArray()).GetProperty("codec_type").GetString());
            }
            foreach(var (path,frames,blueFrames) in new[]{(gif.FinalPath!,9,5),(mp4.FinalPath!,18,9)})
            {
                var pixels=await RecordingContentExportIntegrationTests.Run(ffmpeg,["-i",path,"-map","0:v:0","-an","-fps_mode","passthrough","-pix_fmt","rgb24","-f","rawvideo","pipe:1"]);
                Assert.Equal(frames*64*36*3,pixels.Length);
                for(var n=0;n<frames;n++)
                {
                    var center=n*64*36*3+(18*64+32)*3;
                    Assert.InRange(pixels[center+(n<blueFrames?2:0)],220,255);
                    Assert.InRange(pixels[center+(n<blueFrames?0:2)],0,25);
                    Assert.InRange(pixels[n*64*36*3],0,10); // retained position leaves the left corner black
                }
            }
            var audio=await RecordingContentExportIntegrationTests.Run(ffmpeg,["-i",mp4.FinalPath!,"-vn","-ac","1","-ar","48000","-f","f32le","pipe:1"]);
            Assert.Contains(Enumerable.Range(4800,4800),i=>Math.Abs(BitConverter.ToSingle(audio,i*4))>.02);
            Assert.All(Enumerable.Range(20000,4000),i=>Assert.InRange(Math.Abs(BitConverter.ToSingle(audio,i*4)),0,.002));
            Assert.Equal(2,Directory.GetFiles(output).Length); AssertSource(owner);
        }
        finally { root.Delete(true); }
    }

    private static MacProjectMediaProcess Process()=>new(FFmpegDiscovery.FindFFmpegExecutable()!);
    private static void AssertSource(IProjectHandle owner)
    {
        foreach(var source in owner.Current.Sources)
            Assert.Equal(source.Sha256,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(owner.ProjectDirectory,source.RelativePath)))));
    }
    private sealed class InlineProgress(Action<double> report):IProgress<double> { public void Report(double value)=>report(value); }
}
