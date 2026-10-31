// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using X=ScreenRecorder.Platform.Linux.LinuxX11Connection.X;
namespace ScreenRecorder.Platform.Linux;

// Small owned shaped window, no input grab, desktop settings changes or new process.
public sealed class LinuxX11PointerOverlay:ICursorHighlightService,IPresentationPointerOverlay
{
    private sealed class Session {
        internal volatile bool Stop,Running;
        internal readonly TaskCompletionSource<bool> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Thread? Thread;
    }
    private Session? session;
    private bool disposed;
    public bool IsRunning=>session?.Running==true;
    public event EventHandler? OverlayFailed;
    public void Start(CursorEffectMode mode){if(mode==CursorEffectMode.PresentationStick)throw new InvalidOperationException("Use checked presentation startup.");Stop();}
    public async Task<bool> StartPresentationAsync(TeachingPointerOptions options,CancellationToken cancellationToken=default)
    {
        if(disposed)throw new ObjectDisposedException(nameof(LinuxX11PointerOverlay));Stop();
        if(!OperatingSystem.IsLinux()||ScreenRecorder.Media.Capture.ObsRuntime.DefaultBackend("linux-x64")!="x11")return false;
        cancellationToken.ThrowIfCancellationRequested();
        var state=new Session();session=state;
        state.Thread=new(()=>Run(state,TeachingPointerArtwork.Get(options))){IsBackground=true,Name="OpenCam X11 pointer"};state.Thread.Start();
        try{return await state.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3),cancellationToken).ConfigureAwait(false);}
        catch{state.Stop=true;Stop();if(cancellationToken.IsCancellationRequested)throw new OperationCanceledException(cancellationToken);return false;}
    }
    private void Run(Session state,TeachingPointerBitmap art)
    {
        try {
            using var display=new LinuxX11Connection();var c=display.Handle;
            // Accept only the known 24-bit little-endian TrueColor pixel layout.
            if(!BitConverter.IsLittleEndian||Marshal.ReadByte(display.Setup,30)!=0||Marshal.ReadByte(display.Screen,38)!=24)
                throw new IOException("Unsupported X11 pointer visual.");
            var formats=X.Formats(display.Setup);var format=false;
            while(formats.Rem>0){if(Marshal.ReadByte(formats.Data)==24&&Marshal.ReadByte(formats.Data,1)==32)format=true;X.NextFormat(ref formats);}
            if(!format)throw new IOException("Unsupported X11 pointer pixel format.");
            var raster=LinuxPointerRaster.Create(art);var pixmap=display.Id();var gc=display.Id();var window=display.Id();
            display.Check(X.Pixmap(c,24,pixmap,display.Root,(ushort)art.Width,(ushort)art.Height));
            display.Check(X.Gc(c,gc,pixmap,0,[]));
            display.Check(X.Image(c,2,pixmap,gc,(ushort)art.Width,(ushort)art.Height,0,0,0,24,(uint)raster.Bgrx.Length,raster.Bgrx));
            display.Check(X.Window(c,24,window,display.Root,0,0,(ushort)art.Width,(ushort)art.Height,0,1,
                (uint)Marshal.ReadInt32(display.Screen,32),1|512,[pixmap,1])); // Background pixmap, override-redirect.
            display.Shape(window,0,raster.Rectangles);display.Input(window,false);
            var mapped=false;var hidden=false;
            try {
                while(!state.Stop) {
                    var pointer=X.PointerReply(c,X.Pointer(c,display.Root),out var error);
                    if(error!=IntPtr.Zero)X.Free(error);
                    if(pointer==IntPtr.Zero||error!=IntPtr.Zero) {if(pointer!=IntPtr.Zero)X.Free(pointer);throw new IOException("X11 pointer query failed.");}
                    bool same;short x,y;
                    try{same=Marshal.ReadByte(pointer,1)!=0;x=Marshal.ReadInt16(pointer,16);y=Marshal.ReadInt16(pointer,18);}finally{X.Free(pointer);}
                    if(state.Stop)break;
                    if(same) {
                        display.Check(X.Configure(c,window,1|2|64,[unchecked((uint)(x-raster.HotspotX)),unchecked((uint)(y-raster.HotspotY)),0]));
                        if(!mapped){display.Check(X.Map(c,window));mapped=true;}
                        if(state.Stop)break;
                        if(!hidden){display.Check(X.HideCursor(c,display.Root));hidden=true;}
                        state.Running=true;state.Ready.TrySetResult(true);
                    }else {
                        if(hidden){display.Check(X.ShowCursor(c,display.Root));hidden=false;}
                        if(mapped){display.Check(X.Unmap(c,window));mapped=false;}
                    }
                    Thread.Sleep(8);
                }
            }finally {
                if(hidden)display.Check(X.ShowCursor(c,display.Root));
                // Connection teardown also destroys the window/pixmap/regions/GC,
                // and XFixes specifies outstanding HideCursor requests are removed.
            }
        }catch(Exception e) {
            Serilog.Log.Warning("Optional X11 teaching pointer stopped: ErrorType={ErrorType}",e.GetType().Name);
            if(state.Running&&!state.Stop)try{OverlayFailed?.Invoke(this,EventArgs.Empty);}catch(Exception callback){Serilog.Log.Warning("Pointer notice failed: {ErrorType}",callback.GetType().Name);}
        }finally{state.Running=false;state.Ready.TrySetResult(false);}
    }
    public void Stop(){var state=session;if(state==null)return;state.Stop=true;if(state.Thread?.Join(2000)==false)Serilog.Log.Warning("Optional X11 pointer cleanup awaits the display server.");session=null;}
    public void Dispose(){if(disposed)return;disposed=true;Stop();}
}
