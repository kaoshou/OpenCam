// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectExportProgressTests
{
    [NativeMediaTheory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task SingleClipReportsActualEncodingProgressBeforeTheClipJobReturns(bool audio,bool longClip)
    {
        var root=Directory.CreateTempSubdirectory("OpenCam-export-progress-");
        try
        {
            await using var owner=longClip?await LongClip(root.FullName):await RecordingContentExportIntegrationTests.SingleClip(root.FullName,audio);
            var progress=new ObservedProgress();
            var ffmpeg=FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess native=OperatingSystem.IsWindows()?new WindowsProjectMediaProcess(ffmpeg):new MacProjectMediaProcess(ffmpeg);
            // Deliberately offer only the render interface. Real FFmpeg still does
            // all encoding and verification; this observes its return boundary.
            var observed=new EncodingObserver(native,progress);
            var destination=Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            var result=await new ProjectFfmpegExporter(observed).ExportAsync(owner,owner.Current,destination,
                Guid.NewGuid(),progress,default);
            Assert.True(result.Success,result.Error);Assert.True(observed.Encoded);
            Assert.Contains(RecordingExportPhase.Packaging,progress.Phases);
            Assert.Contains(RecordingExportPhase.Verifying,progress.Phases);
            var values=progress.Values.ToArray();
            Assert.Equal(1,values[^1]);Assert.All(values,v=>Assert.InRange(v,0,1));
            Assert.True(values.Zip(values.Skip(1)).All(pair=>pair.First<=pair.Second));
            if(longClip)Assert.True(values.Count(v=>v>0&&v<.72)>1,"Long encoding must publish multiple updates, not only its final frame");
            var source=owner.Current.Sources.Single();
            Assert.Equal(source.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(owner.ProjectDirectory,source.RelativePath)))));
            await RecordingContentExportIntegrationTests.Run(ffmpeg,["-i",result.FinalPath!,"-f","null","-"]);
            Assert.Single(Directory.GetFiles(destination));
        }
        finally{root.Delete(true);}
    }
    [NativeMediaTheory]
    [InlineData(true)]
    public async Task CancelFromLiveProgressPreservesSourceAndCleansTemporaryFiles(bool _)
    {
        var root=Directory.CreateTempSubdirectory("OpenCam-export-progress-cancel-");
        try
        {
            await using var owner=await LongClip(root.FullName);
            using var cancellation=new CancellationTokenSource();
            var progress=new ObservedProgress{OnValue=value=>{if(value>0)cancellation.Cancel();}};
            var ffmpeg=FFmpegDiscovery.FindFFmpegExecutable()!;
            IProjectMediaProcess native=OperatingSystem.IsWindows()?new WindowsProjectMediaProcess(ffmpeg):new MacProjectMediaProcess(ffmpeg);
            var destination=Directory.CreateDirectory(Path.Combine(root.FullName,"out")).FullName;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ProjectFfmpegExporter(new EncodingObserver(native,progress))
                .ExportAsync(owner,owner.Current,destination,Guid.NewGuid(),progress,cancellation.Token));
            Assert.Contains(progress.Values,v=>v>0);Assert.DoesNotContain(1d,progress.Values);
            Assert.Empty(Directory.GetFiles(destination));
            var source=owner.Current.Sources.Single();
            Assert.Equal(source.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(owner.ProjectDirectory,source.RelativePath)))));
        }
        finally{root.Delete(true);}
    }
    private static async Task<ScreenRecorder.Core.Projects.IProjectHandle> LongClip(string directory)
    {
        var owner=await new JsonProjectStore().CreateAsync(directory,"Long progress fixture");
        try
        {
            var path=Path.Combine(owner.ProjectDirectory,"sources/long.mkv");
            await RecordingContentExportIntegrationTests.Run(FFmpegDiscovery.FindFFmpegExecutable()!,
                ["-f","lavfi","-i","testsrc2=s=1280x720:r=30:d=12","-f","lavfi","-i","sine=frequency=440:sample_rate=48000:duration=12",
                    "-c:v","libx264","-preset","ultrafast","-bf","0","-c:a","aac","-ac","2",path]);
            var id=Guid.NewGuid();
            await owner.SaveAsync(owner.Current with{Revision=1,Canvas=new(1280,720,new(30,1)),Sessions=["s"],
                Sources=[new(){Id=id,SessionId="s",RelativePath="sources/long.mkv",Width=1280,Height=720,
                    VideoCodec="h264",AudioCodec="aac",Timing=new(new(1,1000),0,12000),FileSize=new FileInfo(path).Length,
                    Sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)))}],
                Clips=[new(){Id=Guid.NewGuid(),SourceId=id,Name="Long clip",InPts=0,OutPts=12000}]},0);
            return owner;
        }
        catch{await owner.DisposeAsync();throw;}
    }
    private sealed class ObservedProgress:IRecordingExportProgress
    {
        public ConcurrentQueue<double> Values{get;}=new();
        public ConcurrentQueue<RecordingExportPhase> Phases{get;}=new();
        public Action<double>? OnValue{get;init;}
        public void Report(double value){Values.Enqueue(value);OnValue?.Invoke(value);}
        public void ReportPhase(RecordingExportPhase phase)=>Phases.Enqueue(phase);
    }
    private sealed class EncodingObserver(IProjectMediaProcess native,ObservedProgress progress):IProjectMediaProcess
    {
        public bool Encoded{get;private set;}
        public async Task<ProjectMediaResult> RunAsync(ProjectMediaJob job,IReadOnlyList<FileStream> inputs,FileStream? output,CancellationToken ct)
        {
            var result=await native.RunAsync(job,inputs,output,ct);
            Assert.DoesNotContain(1d,progress.Values);
            if(job.FileDescriptorArguments().Contains("libx264"))
            {
                Encoded=true;
                Assert.Contains(progress.Values,v=>v>0&&v<.8);
            }
            return result;
        }
    }
}
