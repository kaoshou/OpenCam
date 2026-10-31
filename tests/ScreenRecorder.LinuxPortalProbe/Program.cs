// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

if(args.Length==2&&args[0]=="--x11-hotkeys")return await X11Hotkeys.RunAsync(args[1]);
if(args.Length==2&&args[0]=="--x11-pointer")return await X11Pointer.RunAsync(args[1]);
if(!OperatingSystem.IsLinux()||args.Length!=1||Environment.GetEnvironmentVariable("OPENCAM_DISPOSABLE_CI_DESKTOP")!="1")return 2;
var evidence=Path.GetFullPath(args[0]);Directory.CreateDirectory(evidence);
var outcomes=new List<object>();
try {
    var sources=LinuxPortalSources.Shared;
    await using var selection=new LinuxSourceSelectionService(new(),()=>true,ct=>sources.PrepareAsync(LinuxDesktopCatalog.RuntimeRoot,ct));
    var key=AuthenticatedIpc.CreateKey();
    var pipe="opencam-portal-probe-"+Guid.NewGuid().ToString("N");
    await using var server=new NamedPipeIpcServer(pipe,key,selection.DispatchAsync);
    server.Start();
    await using var client=new NamedPipeIpcClient(pipe,key,Environment.ProcessId);
    System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
    var operation=Guid.NewGuid();
    var reply=await client.SendCommandAsync("BeginLinuxSourceSelection",new LinuxSourceSelectionRequest(operation));
    if(!reply.Success)throw new IOException("begin_failed");
    var status=JsonSerializer.Deserialize<LinuxSourceSelectionSnapshot>(reply.DataJson!)!;
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(125));
    while(status.Phase=="selecting") {
        await Task.Delay(200,timeout.Token);
        reply=await client.SendCommandAsync("GetLinuxSourceSelection",new LinuxSourceSelectionRequest(operation,status.ServerInstanceId),cancellationToken:timeout.Token);
        if(!reply.Success)throw new IOException("poll_failed");
        status=JsonSerializer.Deserialize<LinuxSourceSelectionSnapshot>(reply.DataJson!)!;
    }
    if(status.Phase!="ready"||status.Selection is not {} view)throw new IOException("selection_failed");
    if(reply.DataJson!.Contains("restoreToken",StringComparison.OrdinalIgnoreCase))throw new IOException("credential_in_ipc");
    var runtime=ObsRuntime.TryLoad(LinuxDesktopCatalog.RuntimeRoot,"linux-x64")??throw new IOException("runtime_failed");
    var displays=new LinuxDesktopCatalog();
    if(displays.GetMonitors().Single().DeviceName!=view.SourceId)throw new IOException("source_identity_failed");
    CaptureSelection? pinned=null;
    for(var index=0;index<3;index++) {
        var crop=index==2;
        var bounds=crop?new CaptureRegion(40,30,(view.Width/2)&~1,(view.Height/2)&~1):new(0,0,view.Width&~1,view.Height&~1);
        var config=new RecordingConfiguration {Fps=20,AudioSource=AudioSourceType.None,CaptureSource=crop?CaptureSourceType.CustomRegion:CaptureSourceType.Monitor,
            MonitorIndex=0,Region=bounds,EncoderType=HardwareEncoderType.SoftwareCpu,CursorEffect=CursorEffectMode.Default};
        await using var output=new FileStream(Path.Combine(evidence,$"managed-{index}.mkv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,81920,FileOptions.Asynchronous);
        await using(var engine=new LinuxRecordingEngine(runtime,displays,crop?null:pinned)) {
            engine.OutputStream=output;
            await engine.StartRecordingAsync("unused",config,bounds,timeout.Token);
            if(!engine.IsRunning||engine.ActiveCapture?.Backend!=CaptureBackend.PipeWire||engine.ActiveCapture.Bounds!=bounds)throw new IOException("capture_identity_failed");
            if(!crop)pinned=engine.ActiveCapture;
            await Task.Delay(3000,timeout.Token);
            await engine.StopRecordingAsync(timeout.Token);
            if(engine.CurrentFramesRecorded<40||output.Length==0)throw new IOException("media_coverage_failed");
            outcomes.Add(new {index,frames=engine.CurrentFramesRecorded,width=bounds.Width,height=bounds.Height,bytes=output.Length,source="verified_pipewire"});
        }
    }
    await File.WriteAllTextAsync(Path.Combine(evidence,"managed-result.json"),JsonSerializer.Serialize(new {status="PASS",scope="actual virtual GNOME IPC/private-client/engine restore and crop",segments=outcomes}));
    return 0;
}catch(Exception e) {
    // Do not serialize exceptions or a private grant through the test artifact.
    await File.WriteAllTextAsync(Path.Combine(evidence,"managed-result.json"),JsonSerializer.Serialize(new {status="FAIL",errorType=e.GetType().Name,segments=outcomes}));
    return 1;
}
