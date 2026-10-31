// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Linux;

// Private XCB connection, checked passive grabs only. Never captures arbitrary
// keyboard input or changes the user's desktop shortcut settings.
public sealed class LinuxX11HotkeyService : IGlobalHotkeyService
{
    private sealed class Work(int id,int key,uint modifiers,bool remove) {
        internal readonly int Id=id,Key=key;internal readonly uint Modifiers=modifiers;internal readonly bool Remove=remove;
        internal volatile bool Cancelled;
        internal readonly TaskCompletionSource<bool> Done=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record Binding(byte Key,ushort Modifiers,ushort[] Masks);
    private readonly ConcurrentQueue<Work> queue=new();
    private readonly object submit=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread thread;
    private readonly Dictionary<int,Binding> bindings=[];
    private readonly Dictionary<uint,byte> keys=[];
    private readonly HashSet<byte> pressed=[];
    private readonly Dictionary<byte,(uint Time,long Clock)> releases=[];
    private IntPtr connection;
    private uint root;
    private ushort locks=2;
    private volatile bool stopped,disposed;
    public event EventHandler? HotkeyPressed;
    public event EventHandler<int>? HotkeyTriggered;
    public event EventHandler? Unavailable;
    public LinuxX11HotkeyService()
    {
        if(!OperatingSystem.IsLinux()||ScreenRecorder.Media.Capture.ObsRuntime.DefaultBackend("linux-x64")!="x11")
            throw new PlatformNotSupportedException("X11 shortcuts are not global Wayland shortcuts.");
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread=new(()=>Loop(ready)){IsBackground=true,Name="OpenCam X11 shortcuts"};thread.Start();
        if(!ready.Task.Wait(TimeSpan.FromSeconds(2))||!ready.Task.Result){stopped=true;wake.Set();throw new IOException("X11 shortcut connection unavailable.");}
    }
    public bool RegisterHotkey(int key,uint modifiers=0)=>RegisterHotkey(9001,key,modifiers);
    public bool RegisterHotkey(int id,int key,uint modifiers=0)=>Change(new(id,key,modifiers,false));
    public void UnregisterHotkey()=>UnregisterHotkey(9001);
    public void UnregisterHotkey(int id){if(!Change(new(id,0,0,true)))throw new IOException("X11 shortcut could not be released.");}
    private bool Change(Work work)
    {
        lock(submit){if(disposed||stopped)return false;queue.Enqueue(work);wake.Set();}
        if(work.Done.Task.Wait(TimeSpan.FromSeconds(2)))return work.Done.Task.Result;
        work.Cancelled=true;lock(submit){if(!disposed&&!stopped)wake.Set();}return false;
    }
    private void Loop(TaskCompletionSource<bool> ready)
    {
        try {
            connection=X.Connect(IntPtr.Zero,out var screen);
            if(connection==IntPtr.Zero||X.Error(connection)!=0)throw new IOException();
            var setup=X.Setup(connection);var iterator=X.Roots(setup);
            for(var i=0;i<screen&&iterator.Rem>0;i++)X.NextScreen(ref iterator);
            if(iterator.Rem<=0)throw new IOException();
            root=(uint)Marshal.ReadInt32(iterator.Data);
            var min=Marshal.ReadByte(setup,34);var max=Marshal.ReadByte(setup,35);
            var map=X.KeyboardReply(connection,X.Keyboard(connection,min,(byte)(max-min+1)),out var error);
            if(error!=IntPtr.Zero){X.Free(error);throw new IOException();}
            if(map==IntPtr.Zero)throw new IOException();
            try {
                var per=Marshal.ReadByte(map,1);var symbols=X.Keysyms(map);
                for(var code=(int)min;code<=max;code++)for(var n=0;n<per;n++) {
                    var symbol=(uint)Marshal.ReadInt32(symbols,((code-min)*per+n)*4);
                    if(symbol!=0)keys.TryAdd(symbol,(byte)code);
                }
            }finally{X.Free(map);}
            var modifiers=X.ModifierReply(connection,X.Modifier(connection),out error);
            if(error!=IntPtr.Zero){X.Free(error);throw new IOException();}
            if(modifiers==IntPtr.Zero)throw new IOException();
            try {
                var per=Marshal.ReadByte(modifiers,1);var codes=X.ModifierKeys(modifiers);
                if(keys.TryGetValue(0xFF7F,out var num))for(var m=0;m<8;m++)for(var n=0;n<per;n++)
                    if(Marshal.ReadByte(codes,m*per+n)==num)locks|=(ushort)(1<<m);
            }finally{X.Free(modifiers);}
            ready.TrySetResult(true);
            while(!stopped) {
                while(queue.TryDequeue(out var work)) {
                    if(work.Cancelled){work.Done.TrySetResult(false);continue;}
                    var result=Apply(work);
                    if(work.Cancelled&&result&&!work.Remove)Remove(work.Id);
                    work.Done.TrySetResult(result&&!work.Cancelled);
                }
                IntPtr e;
                while((e=X.Poll(connection))!=IntPtr.Zero) {
                    try{Event(e);}finally{X.Free(e);}
                }
                foreach(var pair in releases.ToArray())if(Environment.TickCount64-pair.Value.Clock>=30){pressed.Remove(pair.Key);releases.Remove(pair.Key);}
                if(X.Error(connection)!=0)throw new IOException();
                wake.WaitOne(5);
            }
        }catch(Exception error) {
            Serilog.Log.Warning("X11 shortcuts unavailable: ErrorType={ErrorType}",error.GetType().Name);
            try{Unavailable?.Invoke(this,EventArgs.Empty);}catch(Exception callback){Serilog.Log.Warning("Shortcut notice failed: {ErrorType}",callback.GetType().Name);}
        }finally {
            stopped=true;ready.TrySetResult(false);
            if(connection!=IntPtr.Zero){X.Disconnect(connection);connection=IntPtr.Zero;} // Server releases all this client's grabs.
            while(queue.TryDequeue(out var work))work.Done.TrySetResult(false);
        }
    }
    private bool Check(X.Cookie cookie)
    {
        var error=X.Check(connection,cookie);if(error!=IntPtr.Zero)X.Free(error);
        return error==IntPtr.Zero&&X.Error(connection)==0;
    }
    private bool Apply(Work work)
    {
        if(work.Remove)return Remove(work.Id);
        ushort modifiers;
        try{modifiers=LinuxHotkeyMap.Modifiers(work.Modifiers);}catch(ArgumentException){return false;}
        if(work.Id<=0||!keys.TryGetValue(LinuxHotkeyMap.Keysym(work.Key),out var key)||
            bindings.Any(p=>p.Key!=work.Id&&p.Value.Key==key&&p.Value.Modifiers==modifiers))return false;
        if(!Remove(work.Id))return false;
        var masks=new[]{(ushort)0,(ushort)2,(ushort)(locks&~2),locks}.Distinct().Select(v=>(ushort)(modifiers|v)).Distinct().ToArray();
        foreach(var mask in masks)if(!Check(X.Grab(connection,0,root,mask,key,1,1))) {
            foreach(var rollback in masks)Check(X.Ungrab(connection,key,root,rollback));
            return false;
        }
        bindings.Add(work.Id,new(key,modifiers,masks));return true;
    }
    private bool Remove(int id)
    {
        if(!bindings.Remove(id,out var binding))return true;
        var ok=true;foreach(var mask in binding.Masks)ok=Check(X.Ungrab(connection,binding.Key,root,mask))&&ok;
        pressed.Remove(binding.Key);releases.Remove(binding.Key);return ok;
    }
    private void Event(IntPtr e)
    {
        var type=Marshal.ReadByte(e)&127;
        if(type==34) {
            foreach(var bindingId in bindings.Keys.ToArray())Remove(bindingId);
            Unavailable?.Invoke(this,EventArgs.Empty);return;
        }
        if(type is not(2 or 3))return;
        var key=Marshal.ReadByte(e,1);var time=(uint)Marshal.ReadInt32(e,4);
        if(type==3){releases[key]=(time,Environment.TickCount64);return;}
        if(releases.Remove(key,out var release)){if(release.Time==time)return;pressed.Remove(key);}
        if(!pressed.Add(key))return;
        var modifiers=(ushort)((ushort)Marshal.ReadInt16(e,28)&255&~locks);
        var id=bindings.FirstOrDefault(p=>p.Value.Key==key&&p.Value.Modifiers==modifiers).Key;
        if(id==0)return;
        try{HotkeyPressed?.Invoke(this,EventArgs.Empty);HotkeyTriggered?.Invoke(this,id);}
        catch(Exception error){Serilog.Log.Warning("Shortcut callback failed: ErrorType={ErrorType}",error.GetType().Name);}
    }
    public void Dispose(){lock(submit){if(disposed)return;disposed=true;stopped=true;wake.Set();}if(thread.Join(2000))wake.Dispose();else Serilog.Log.Warning("X11 shortcut shutdown is waiting for the display server.");}
    private static class X
    {
        private const string Library="libxcb.so.1";
        [StructLayout(LayoutKind.Sequential)]internal struct Cookie{internal uint Sequence;}
        [StructLayout(LayoutKind.Sequential)]internal struct Iterator{internal IntPtr Data;internal int Rem,Index;}
        [DllImport(Library,EntryPoint="xcb_connect")]internal static extern IntPtr Connect(IntPtr display,out int screen);
        [DllImport(Library,EntryPoint="xcb_disconnect")]internal static extern void Disconnect(IntPtr connection);
        [DllImport(Library,EntryPoint="xcb_connection_has_error")]internal static extern int Error(IntPtr connection);
        [DllImport(Library,EntryPoint="xcb_get_setup")]internal static extern IntPtr Setup(IntPtr connection);
        [DllImport(Library,EntryPoint="xcb_setup_roots_iterator")]internal static extern Iterator Roots(IntPtr setup);
        [DllImport(Library,EntryPoint="xcb_screen_next")]internal static extern void NextScreen(ref Iterator iterator);
        [DllImport(Library,EntryPoint="xcb_get_keyboard_mapping")]internal static extern Cookie Keyboard(IntPtr c,byte first,byte count);
        [DllImport(Library,EntryPoint="xcb_get_keyboard_mapping_reply")]internal static extern IntPtr KeyboardReply(IntPtr c,Cookie cookie,out IntPtr error);
        [DllImport(Library,EntryPoint="xcb_get_keyboard_mapping_keysyms")]internal static extern IntPtr Keysyms(IntPtr reply);
        [DllImport(Library,EntryPoint="xcb_get_modifier_mapping")]internal static extern Cookie Modifier(IntPtr c);
        [DllImport(Library,EntryPoint="xcb_get_modifier_mapping_reply")]internal static extern IntPtr ModifierReply(IntPtr c,Cookie cookie,out IntPtr error);
        [DllImport(Library,EntryPoint="xcb_get_modifier_mapping_keycodes")]internal static extern IntPtr ModifierKeys(IntPtr reply);
        [DllImport(Library,EntryPoint="xcb_grab_key_checked")]internal static extern Cookie Grab(IntPtr c,byte events,uint window,ushort modifiers,byte key,byte pointerMode,byte keyboardMode);
        [DllImport(Library,EntryPoint="xcb_ungrab_key_checked")]internal static extern Cookie Ungrab(IntPtr c,byte key,uint window,ushort modifiers);
        [DllImport(Library,EntryPoint="xcb_request_check")]internal static extern IntPtr Check(IntPtr c,Cookie cookie);
        [DllImport(Library,EntryPoint="xcb_poll_for_event")]internal static extern IntPtr Poll(IntPtr c);
        [DllImport("libc",EntryPoint="free")]internal static extern void Free(IntPtr p);
    }
}
