// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreenRecorder.Platform.Linux;

if(args.Length!=1)return 2;
if(args[0]=="unsupported") {
    if(OperatingSystem.IsLinux())return 2;
    var nativeCalls=0;
    NativeLibrary.SetDllImportResolver(typeof(LinuxTeachingBridgeTransport).Assembly,(_name,_assembly,_path)=>{
        Interlocked.Increment(ref nativeCalls);throw new InvalidOperationException("Unexpected native access");
    });
    await using var unsupported=new LinuxTeachingBridgeTransport();
    try {await unsupported.CallAsync("Capabilities","",CancellationToken.None);return 1;}
    catch(IOException e) {if(nativeCalls!=0||e.InnerException is not null)return 1;}
    Console.WriteLine("unsupported host made no native calls");return 0;
}
if(args[0]!="connected"||Environment.GetEnvironmentVariable("OPENCAM_DISPOSABLE_CI_DESKTOP")!="1")return 2;
await using(var transport=new LinuxTeachingBridgeTransport()) {
    var received=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    transport.SignalReceived+=(_sender,text)=>{
        using var doc=JsonDocument.Parse(text);
        if(doc.RootElement.GetProperty("event").GetString()=="probe")received.TrySetResult(true);
    };
    var reply=LinuxTeachingReply.Parse(await transport.CallAsync("Capabilities","",CancellationToken.None));
    if(!reply.ReadCapabilities().Supported)return 1;
    _=await transport.CallAsync("Status","",CancellationToken.None);
    if(!await received.Task.WaitAsync(TimeSpan.FromSeconds(3)))return 1;
    using var canceled=new CancellationTokenSource();canceled.Cancel();
    try {await transport.CallAsync("Capabilities","",canceled.Token);return 1;}
    catch(OperationCanceledException) {}
    // Cancel an actual in-flight GIO call waiting for the real consent dialog.
    using var inFlight=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    try {await transport.CallAsync("Connect","",inFlight.Token);return 1;}
    catch(OperationCanceledException) {}
}
Console.WriteLine("managed teaching transport checks passed");return 0;
