// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [MacOsOnlyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerPreview_StillCacheIncludesStyleAndRevalidatesTrackBeforeEveryHit(bool thumbnail)
    {
        await using var scope = new RecordingScope();
        await using var data = await PointerPreviewFixture.CreateAsync(scope.Configuration.OutputDirectory);
        await data.ReleaseOwnerAsync();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var opened = await coordinator.OpenAsync(data.Manifest);
        Assert.True(opened.Success, opened.ErrorCode);
        var process = new PointerCountingProcess(data.Backend.CreateProcess());
        await using var frames = new ProjectFrameService(coordinator, process);
        var card = thumbnail ? coordinator.Current!.Clips[0].Id : Guid.Empty;
        var first = await PointerStillAsync(frames, coordinator.Current!, card);
        AssertPointer(first.Rgba!, first.PixelWidth, thumbnail ? 144 : 90, thumbnail ? 96 : 60);
        Assert.Equal(1, process.Calls);
        await frames.SuspendAsync();
        var cached = await PointerStillAsync(frames, coordinator.Current!, card);
        Assert.Equal(first.Rgba, cached.Rgba);
        Assert.Equal(1, process.Calls);

        Assert.True((await coordinator.SetPointerSettingsAsync(new(ProjectPointerStyle.Hidden,200), coordinator.Current!.Revision)).Success);
        var hidden = await PointerStillAsync(frames, coordinator.Current!, card);
        var hot = ((thumbnail ? 96 : 60) * hidden.PixelWidth + (thumbnail ? 144 : 90)) * 4;
        Assert.InRange(hidden.Rgba![hot], 0, 10);
        Assert.Equal(2, process.Calls);
        Assert.True((await coordinator.SetPointerSettingsAsync(new(ProjectPointerStyle.Dot,200), coordinator.Current!.Revision)).Success);
        var restored = await PointerStillAsync(frames, coordinator.Current!, card);
        Assert.Equal(first.Rgba, restored.Rgba);
        Assert.Equal(2, process.Calls);

        var bytes = await File.ReadAllBytesAsync(data.TrackPath); bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(data.TrackPath, bytes);
        var rejectedMemory = await frames.QueryAsync(coordinator.Current!.ProjectId, coordinator.Current.Revision,
            0, card, ProjectPreviewQuality.Original);
        Assert.Null(rejectedMemory.Rgba); Assert.NotNull(rejectedMemory.Error);
        await frames.SuspendAsync();
        var rejectedDisk = await PointerStillAsync(frames, coordinator.Current!, card, expectError: true);
        Assert.Null(rejectedDisk.Rgba); Assert.NotNull(rejectedDisk.Error);
        Assert.Equal(2, process.Calls);
    }

    [MacOsOnlyFact]
    public async Task PointerPreview_StreamingSeekUsesOriginalTrimmedPlanAndPreservesAudioScheduling()
    {
        await using var scope = new RecordingScope();
        await using var data = await PointerPreviewFixture.CreateAsync(scope.Configuration.OutputDirectory);
        var audio = new PointerMeasuringAudio();
        var settings = new ProjectPreviewSettings(new(320,180));
        Func<ProjectSource,CancellationToken,Task<PointerTrackCursor>> open = (source,ct) =>
            PointerTrackCursor.OpenAsync(data.Owner!,source,ct);
        // Reflection keeps the RED executable against the existing settings type.
        typeof(ProjectPreviewSettings).GetProperty("OpenPointerCursor")?.SetValue(settings,open);
        var decoder = new ProjectPreviewDecoder(data.Backend.CreateStreamingProcess, () => audio);
        var seen = new List<ProjectPreviewFrame>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await decoder.PlayConfiguredAsync(data.Owner!.Current,500_000,settings,
            (_,_)=>Task.FromResult(File.OpenRead(data.SourcePath)), frame=> { seen.Add(frame); return Task.CompletedTask; }, _=>{},deadline.Token);
        Assert.Equal(3,seen.Count);
        for(var i=0;i<3;i++) AssertPointer(seen[i].Rgba,320,100+i*10,60);
        Assert.Equal(7200,audio.PositionSamples);
        Assert.True(audio.Stopped);
        Assert.Equal(data.Source.Sha256,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(data.SourcePath))));
    }

    [MacOsOnlyFact]
    public async Task PointerPreview_CoordinatorSuppliesVerifiedCursorWithoutChangingPlaybackDelegate()
    {
        await using var scope = new RecordingScope();
        await using var data = await PointerPreviewFixture.CreateAsync(scope.Configuration.OutputDirectory);
        await data.ReleaseOwnerAsync();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder,new JsonProjectStore(),new ProjectProbeStub());
        Assert.True((await coordinator.OpenAsync(data.Manifest)).Success);
        var audio = new PointerMeasuringAudio();
        var decoder = new ProjectPreviewDecoder(data.Backend.CreateStreamingProcess,()=>audio);
        await using var preview = new ProjectPreviewCoordinator(coordinator,decoder.PlayConfiguredAsync,TimeSpan.FromSeconds(10));
        var state = await preview.PlayAsync(coordinator.Current!.ProjectId,coordinator.Current.Revision,500_000,ProjectPreviewQuality.Original);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (ProjectPlaybackState State, ProjectFrameReply? Frame) result;
        do { await Task.Delay(10,deadline.Token); result=preview.Query(state.Generation); } while(result.State.Playing);
        Assert.Null(result.State.Error);
        Assert.NotNull(result.Frame?.Rgba);
        AssertPointer(result.Frame!.Rgba!,320,120,60);
        Assert.Equal(7200,audio.PositionSamples);
        Assert.True(audio.Stopped);
    }

    private static void AssertPointer(byte[] rgba,int width,int x,int y)
    {
        var hot=(y*width+x)*4;
        Assert.True(rgba[hot]>rgba[hot+1]+30,$"Expected red pointer hotspot, got {rgba[hot]},{rgba[hot+1]},{rgba[hot+2]}.");
        Assert.InRange(rgba[(100*width+200)*4],0,10);
    }

    private static async Task<ProjectFrameReply> PointerStillAsync(ProjectFrameService frames,RecordingProject project,
        Guid card,bool expectError=false)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while(true)
        {
            var reply=await frames.QueryAsync(project.ProjectId,project.Revision,0,card,ProjectPreviewQuality.Original);
            if(reply.Error is not null) { if(!expectError) Assert.Fail(reply.Error); return reply; }
            if(reply.HasValidPixels) return reply;
            await Task.Delay(10,deadline.Token);
        }
    }

    private sealed class PointerCountingProcess(IProjectMediaProcess inner):IProjectMediaProcess
    {
        public int Calls;
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job,IReadOnlyList<FileStream> inputs,FileStream? output,CancellationToken ct)
        { Interlocked.Increment(ref Calls); return inner.RunAsync(job,inputs,output,ct); }
    }

    private sealed class PointerMeasuringAudio:IProjectAudioOutput
    {
        public long PositionSamples {get;private set;}
        public bool Stopped;
        public Task StartAsync(CancellationToken ct)=>Task.CompletedTask;
        public Task WriteAsync(ReadOnlyMemory<byte> pcm,CancellationToken ct) {PositionSamples+=pcm.Length/8;return Task.CompletedTask;}
        public Task CompleteAsync(CancellationToken ct)=>Task.CompletedTask;
        public Task StopAsync(CancellationToken ct){Stopped=true;return Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }

    private sealed class PointerPreviewFixture:IAsyncDisposable
    {
        public IProjectHandle? Owner;
        public required ProjectSource Source;
        public required string SourcePath,TrackPath,Manifest;
        public required ProjectMediaBackend Backend;
        public static async Task<PointerPreviewFixture> CreateAsync(string parent)
        {
            var owner=await new JsonProjectStore().CreateAsync(parent,"pointer-preview");
            try
            {
                var ffmpeg=FFmpegDiscovery.FindFFmpegExecutable()!;
                var sourcePath=Path.Combine(owner.ProjectDirectory,"sources/video.mkv");
                await RecordingContentExportIntegrationTests.Run(ffmpeg,["-f","lavfi","-i","color=black:s=320x180:r=20:d=0.3","-c:v","libx264","-bf","0",sourcePath]);
                var id=Guid.NewGuid();
                var source=new ProjectSource {Id=id,SessionId="s",RelativePath="sources/video.mkv",Width=320,Height=180,VideoCodec="h264",
                    FileSize=new FileInfo(sourcePath).Length,Sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))),Timing=new(new(1,1000),0,300)};
                using(var root=BoundDirectory.Open(owner.ProjectDirectory))
                using(var folder=root.OpenChild("pointers",create:true))
                {
                    using(var writer=PointerTrackWriter.Create(folder,$"{id:N}.ocpt",new(owner.Current.ProjectId,id,320,180)))
                    {
                        for(var i=0;i<=6;i++)
                        {
                            writer.Append(new(new PointerSample(1_000_000+i*50_000,80+i*10,60,true,0),null));
                            writer.Append(new(null,new NativeFrameClock(i*50_000,1_000_000+i*50_000,999_000+i*50_000,(ulong)(i+1))));
                        }
                        writer.Seal(new(source.FileSize,source.Sha256));
                    }
                    using var track=folder.Read($"{id:N}.ocpt");
                    source=source with {Pointer=new($"pointers/{id:N}.ocpt",track.Length,Convert.ToHexString(await SHA256.HashDataAsync(track)),0,true)};
                }
                await owner.SaveAsync(owner.Current with {SchemaVersion=5,Revision=1,Canvas=new(320,180,new(20,1)),Sources=[source],Sessions=["s"],
                    PointerSettings=new(ProjectPointerStyle.Dot,200),Clips=[new(){Id=Guid.NewGuid(),SourceId=id,Name="trimmed",InPts=50,OutPts=250}]},0);
                return new(){Owner=owner,Source=source,SourcePath=sourcePath,TrackPath=Path.Combine(owner.ProjectDirectory,source.Pointer!.RelativePath),
                    Manifest=Path.Combine(owner.ProjectDirectory,"project.opencam"),Backend=ProjectMediaBackend.Create(OSPlatform.OSX,ffmpeg,"")!};
            }
            catch {await owner.DisposeAsync();throw;}
        }
        public async Task ReleaseOwnerAsync(){if(Owner is not null) await Owner.DisposeAsync();Owner=null;}
        public async ValueTask DisposeAsync()=>await ReleaseOwnerAsync();
    }
}
