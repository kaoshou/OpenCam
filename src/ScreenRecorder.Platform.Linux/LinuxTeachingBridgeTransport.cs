// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScreenRecorder.Platform.Linux;

internal interface ILinuxTeachingTransport:IAsyncDisposable
{
    event EventHandler<string>? SignalReceived;
    Task<string> CallAsync(string method,string json,CancellationToken cancellationToken);
}

// One private bus connection owned by Recorder. No process launch, display
// mutation, source credentials, or synchronous UI-thread network operations.
internal sealed class LinuxTeachingBridgeTransport:ILinuxTeachingTransport
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly byte[] response=new byte[65537];
    private Context? context;
    private int disposed,queuedEvents;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    public event EventHandler<string>? SignalReceived;
    public async Task<string> CallAsync(string method,string json,CancellationToken cancellationToken)
    {
        var argument=method is "Bind" or "Request";
        if(method is not("Capabilities" or "Connect" or "Challenge" or "Bind" or "CancelChallenge" or "Request" or "Status")||
            json is null||Encoding.UTF8.GetByteCount(json)>65536||!argument&&json.Length!=0)
            throw new IOException("Invalid Wayland teaching integration request.");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed)!=0,this);
            if(!OperatingSystem.IsLinux()||!string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),"wayland",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Wayland teaching integration is unavailable on this desktop.");
            cancellationToken.ThrowIfCancellationRequested();
            context??=await Task.Run(()=>Context.Create(OnNativeEvent),CancellationToken.None).ConfigureAwait(false);
            if(Volatile.Read(ref disposed)!=0)throw new IOException("Wayland teaching integration was disconnected.");
            var owned=context;
            var timeout=method=="Connect"?30000:2000;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(timeout+3000));
            using var registration=deadline.Token.Register(()=>Cancel(owned));
            var result=await Task.Run(()=>{
                var status=Native.Connect(owned);
                return status==0?Native.Call(owned,method,json,timeout,response,(nuint)response.Length):status;
            },CancellationToken.None).ConfigureAwait(false);
            if(deadline.IsCancellationRequested) {
                Reset();cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("Wayland teaching integration timed out.");
            }
            if(result!=0) {Reset();throw new IOException("Wayland teaching integration was unavailable or disconnected.");}
            var length=Array.IndexOf(response,(byte)0);
            if(length<0||length>65536) {Reset();throw new IOException("Invalid Wayland teaching integration response.");}
            return StrictUtf8.GetString(response,0,length);
        }catch(Exception error) when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or DecoderFallbackException) {
            Reset();throw new IOException("Wayland teaching integration is unavailable or invalid.");
        }finally {gate.Release();}
    }
    private static void Cancel(Context owned)
    {
        try {if(!owned.IsClosed&&!owned.IsInvalid)Native.Cancel(owned);}
        catch(ObjectDisposedException) {/* Concurrent cancellation cannot revive a closed connection. */}
    }
    private void Reset(){var owned=context;context=null;owned?.Dispose();Array.Clear(response);}
    private void OnNativeEvent(nint json,nint _user)
    {
        try {
            if(Volatile.Read(ref disposed)!=0||json==0)return;
            var text=Marshal.PtrToStringUTF8(json);
            if(text is null||Encoding.UTF8.GetByteCount(text)>65536)return;
            if(Interlocked.Increment(ref queuedEvents)>128){Interlocked.Decrement(ref queuedEvents);return;}
            // Never execute an observer or dispose/join the GIO thread from its
            // own native callback. The copied string is bounded and private.
            ThreadPool.QueueUserWorkItem(_=>{
                try {if(Volatile.Read(ref disposed)==0)SignalReceived?.Invoke(this,text);}
                catch {/* Observer data/errors must not escape into unmanaged code. */}
                finally {Interlocked.Decrement(ref queuedEvents);}
            });
        }catch {/* No managed exception may cross the native callback boundary. */}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        var owned=Volatile.Read(ref context);if(owned is not null)Cancel(owned);
        await gate.WaitAsync().ConfigureAwait(false);
        try {Reset();SignalReceived=null;}finally {gate.Release();}
    }
    private sealed class Context:SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Native.Event callback;
        private Context(nint value,Native.Event callback):base(true){SetHandle(value);this.callback=callback;}
        internal static Context Create(Native.Event callback)
        {
            var value=Native.Create(callback,0);
            if(value==0)throw new IOException("Wayland teaching integration could not connect.");
            return new(value,callback);
        }
        protected override bool ReleaseHandle()
        {
            try {Native.Close(handle);GC.KeepAlive(callback);return true;}
            catch {GC.KeepAlive(callback);return false;}
        }
    }
    private static class Native
    {
        private const string Library="OpenCamLinuxTeaching";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Event(nint json,nint user);
        [DllImport(Library,EntryPoint="oc_teaching_create",CallingConvention=CallingConvention.Cdecl)]internal static extern nint Create(Event callback,nint user);
        [DllImport(Library,EntryPoint="oc_teaching_connect",CallingConvention=CallingConvention.Cdecl)]internal static extern int Connect(Context context);
        [DllImport(Library,EntryPoint="oc_teaching_call",CallingConvention=CallingConvention.Cdecl)]internal static extern int Call(Context context,
            [MarshalAs(UnmanagedType.LPUTF8Str)]string method,[MarshalAs(UnmanagedType.LPUTF8Str)]string json,int timeout,[Out]byte[] output,nuint capacity);
        [DllImport(Library,EntryPoint="oc_teaching_cancel",CallingConvention=CallingConvention.Cdecl)]internal static extern void Cancel(Context context);
        [DllImport(Library,EntryPoint="oc_teaching_close",CallingConvention=CallingConvention.Cdecl)]internal static extern void Close(nint context);
    }
}
