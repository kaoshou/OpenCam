// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
using System.Diagnostics;
using System.Text.Json;

// Actual OBS recording of the disposable UI fixture, never a video fallback.
internal sealed class AnnotationRecordingFixture:IAsyncDisposable
{
    private readonly LinuxX11PointerOverlay pointer=new();
    private LinuxRecordingEngine? engine;
    private FileStream? output;
    private string path="";
    private CaptureRegion bounds=CaptureRegion.Empty;
    internal static async Task<AnnotationRecordingFixture?> StartAsync(string evidence)
    {
        if(Environment.GetEnvironmentVariable("OPENCAM_TEST_RECORD_ANNOTATIONS")!="1")return null;
        var fixture=new AnnotationRecordingFixture();
        try {
            var runtime=ObsRuntime.TryLoad(AppContext.BaseDirectory,"linux-x64")??throw new IOException("annotation_recording_runtime_missing");
            var displays=new LinuxDesktopCatalog();var monitor=displays.GetMonitors().FirstOrDefault()??throw new IOException("annotation_recording_screen_missing");
            fixture.bounds=monitor.Bounds;
            fixture.path=Path.Combine(evidence,"annotations.mkv");
            fixture.output=new(fixture.path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            fixture.engine=new(runtime,displays,null){OutputStream=fixture.output};
            if(!await fixture.pointer.StartPresentationAsync(new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Standard)))throw new IOException("annotation_recording_pointer_failed");
            await fixture.engine.StartRecordingAsync("unused",new(){Fps=20,AudioSource=AudioSourceType.None,CaptureSource=CaptureSourceType.Monitor,
                MonitorIndex=monitor.Index,CursorEffect=CursorEffectMode.PresentationStick,EncoderType=HardwareEncoderType.SoftwareCpu},fixture.bounds);
            return fixture;
        }catch{await fixture.DisposeAsync();throw;}
    }
    internal async Task SealAndVerifyAsync(string evidence,int pointX,int pointY)
    {
        await engine!.StopRecordingAsync();pointer.Stop();await output!.FlushAsync();
        if(engine.CurrentFramesRecorded<20||output.Length==0)throw new IOException("annotation_recording_no_frames");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"ffmpeg")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-xerror","-v","error","-ss","1","-i",path,"-frames:v","1","-vf",$"crop=12:12:{pointX-bounds.X-6}:{pointY-bounds.Y-6},format=rgb24","-f","rawvideo","-"})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("annotation_decode_start_failed");
        var errors=process.StandardError.ReadToEndAsync(timeout.Token);var pixels=new byte[12*12*3];
        try{await process.StandardOutput.BaseStream.ReadExactlyAsync(pixels,timeout.Token);await process.WaitForExitAsync(timeout.Token);}
        finally{if(!process.HasExited)process.Kill();}
        if(process.ExitCode!=0||!string.IsNullOrEmpty(await errors))throw new IOException("annotation_decode_failed");
        var red=0;for(var i=0;i<pixels.Length;i+=3)if(pixels[i]>140&&pixels[i+1]<120&&pixels[i+2]<140)red++;
        if(red<6)throw new IOException("annotation_marks_missing_from_recording");
        await File.WriteAllTextAsync(Path.Combine(evidence,"annotation-recording-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="actual OBS X11 capture of Avalonia marks with live teaching pointer; virtual desktop, no audio",frames=engine.CurrentFramesRecorded,redPixels=red}));
    }
    public async ValueTask DisposeAsync()
    {
        try{if(engine is not null)await engine.DisposeAsync();}
        finally{pointer.Dispose();if(output is not null)await output.DisposeAsync();}
    }
}
