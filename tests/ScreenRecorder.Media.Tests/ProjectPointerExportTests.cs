// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPointerExportTests
{
    [NativeMediaTheory]
    [InlineData("mp4", "visible")]
    [InlineData("gif", "visible")]
    [InlineData("web", "visible")]
    [InlineData("mp4", "hidden")]
    [InlineData("gif", "hidden")]
    [InlineData("web", "hidden")]
    [InlineData("mp4", "damaged")]
    [InlineData("gif", "damaged")]
    [InlineData("web", "damaged")]
    public async Task OutputHonorsPointerVisibilityAndDamagedTracksNeverPublish(string format, string mode)
    {
        var root=Directory.CreateTempSubdirectory("pointer-export-");
        try
        {
            await using var owner=await new JsonProjectStore().CreateAsync(root.FullName,"pointer");
            var ffmpeg=FFmpegDiscovery.FindFFmpegExecutable()!;
            var path=Path.Combine(owner.ProjectDirectory,"sources/video.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg,["-f","lavfi","-i","color=black:s=320x180:r=20:d=0.3","-c:v","libx264","-bf","0",path]);
            var id=Guid.NewGuid();
            var source=new ProjectSource {Id=id,SessionId="s",RelativePath="sources/video.mkv",Width=320,Height=180,VideoCodec="h264",
                FileSize=new FileInfo(path).Length,Sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),Timing=new(new(1,1000),0,300)};
            using(var bound=BoundDirectory.Open(owner.ProjectDirectory))
            using(var folder=bound.OpenChild("pointers",create:true))
            {
                using(var writer=PointerTrackWriter.Create(folder,$"{id:N}.ocpt",new(owner.Current.ProjectId,id,320,180)))
                {
                    for(var i=0;i<=6;i++)
                    {
                        writer.Append(new(new PointerSample(1_000_000+i*50_000,80,60,true,0),null));
                        writer.Append(new(null,new NativeFrameClock(i*50_000,1_000_000+i*50_000,999_000+i*50_000,(ulong)(i+1))));
                    }
                    writer.Seal(new(source.FileSize,source.Sha256,0));
                }
                using var track=folder.Read($"{id:N}.ocpt");
                source=source with {Pointer=new($"pointers/{id:N}.ocpt",track.Length,Convert.ToHexString(await SHA256.HashDataAsync(track)),0,true)};
            }
            await owner.SaveAsync(owner.Current with {SchemaVersion=5,Revision=1,Canvas=new(320,180,new(20,1)),Sources=[source],Sessions=["s"],
                PointerSettings=new(mode=="hidden"?ProjectPointerStyle.Hidden:ProjectPointerStyle.Dot,200),Clips=[new(){Id=Guid.NewGuid(),Name="clip",SourceId=id,InPts=0,OutPts=300}]},0);
            if(mode!="visible") await File.AppendAllTextAsync(Path.Combine(owner.ProjectDirectory,source.Pointer!.RelativePath),"tamper");
            IProjectMediaProcess process=OperatingSystem.IsWindows()
                ? new WindowsProjectMediaProcess(ffmpeg) : new MacProjectMediaProcess(ffmpeg);
            IRecordingContentExporter exporter=format switch {
                "gif"=>new ProjectGifExporter(process,new(FramesPerSecond:20)),
                "web"=>new ProjectWebPlayerExporter(new ProjectFfmpegExporter(process)),
                _=>new ProjectFfmpegExporter(process)};
            var output=Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            if(mode=="damaged")
            {
                await Assert.ThrowsAsync<InvalidDataException>(()=>exporter.ExportAsync(owner,owner.Current,output,Guid.NewGuid(),new Progress<double>(),default));
                // The web exporter may retain its empty owned folder, but must never
                // leave media, an entry page, or a misleading completed artifact.
                Assert.Empty(Directory.GetFiles(output,"*",SearchOption.AllDirectories));
                Assert.Equal(source.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
                return;
            }
            var result=await exporter.ExportAsync(owner,owner.Current,output,Guid.NewGuid(),new Progress<double>(),default);
            Assert.True(result.Success,result.Error);
            var media=format=="web"?Path.Combine(Path.GetDirectoryName(result.FinalPath!)!,"media.mp4"):result.FinalPath!;
            var pixels=await RecordingContentExportIntegrationTests.Run(ffmpeg,["-i",media,"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","pipe:1"]);
            Assert.Equal(320*180*3,pixels.Length);
            var hot=(60*320+80)*3;
            if(mode=="visible") Assert.True(pixels[hot]>pixels[hot+1]+30,$"Pointer absent in {format}: {pixels[hot]}, {pixels[hot+1]}, {pixels[hot+2]}");
            else Assert.InRange(pixels[hot],0,10);
            Assert.InRange(pixels[(100*320+200)*3],0,10);
            Assert.Equal(source.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
        finally {root.Delete(true);}
    }
}
