// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
namespace ScreenRecorder.Platform.Linux;

// Every instance owns a separate connection. No process-wide Xlib error handler.
internal sealed class LinuxX11Connection:IDisposable
{
    internal IntPtr Handle{get;private set;}
    internal readonly IntPtr Setup,Screen;
    internal readonly uint Root;
    internal LinuxX11Connection()
    {
        if(!OperatingSystem.IsLinux()||ScreenRecorder.Media.Capture.ObsRuntime.DefaultBackend("linux-x64")!="x11")throw new PlatformNotSupportedException();
        try {
            Handle=X.Connect(IntPtr.Zero,out var screen);
            if(Handle==IntPtr.Zero||X.Error(Handle)!=0)throw new IOException("X11 connection unavailable.");
            Setup=X.Setup(Handle);var iterator=X.Roots(Setup);for(var i=0;i<screen&&iterator.Rem>0;i++)X.Next(ref iterator);
            if(iterator.Rem<=0)throw new IOException("X11 screen unavailable.");Screen=iterator.Data;Root=(uint)Marshal.ReadInt32(Screen);
            var reply=X.VersionReply(Handle,X.Version(Handle,4,0),out var error);
            if(error!=IntPtr.Zero)X.Free(error);
            if(reply==IntPtr.Zero)throw new IOException("XFixes unavailable.");
            try{if(error!=IntPtr.Zero||Marshal.ReadInt32(reply,8)<4)throw new IOException("XFixes 4 required.");}finally{X.Free(reply);}
        }catch{Dispose();throw;}
    }
    internal uint Id()=>X.Id(Handle);
    internal void Check(X.Cookie cookie)
    {
        var error=X.Check(Handle,cookie);
        var code=error==IntPtr.Zero?0:Marshal.ReadByte(error,1);if(error!=IntPtr.Zero)X.Free(error);
        if(error!=IntPtr.Zero||X.Error(Handle)!=0)throw new IOException($"X11 optional overlay request failed (code={code}).");
    }
    internal void Shape(uint window,byte kind,byte[] rectangles)
    {
        var region=Id();Check(X.Region(Handle,region,(uint)(rectangles.Length/8),rectangles));
        try{Check(X.Shape(Handle,window,kind,0,0,region));}finally{Check(X.DestroyRegion(Handle,region));}
    }
    internal void Input(uint window,bool interactive)
    {
        if(interactive)Check(X.Shape(Handle,window,2,0,0,0));else Shape(window,2,[]);
    }
    public void Dispose(){if(Handle!=IntPtr.Zero){X.Disconnect(Handle);Handle=IntPtr.Zero;}}
    internal static class X
    {
        const string C="libxcb.so.1",F="libxcb-xfixes.so.0";
        [StructLayout(LayoutKind.Sequential)]internal struct Cookie{internal uint Sequence;}
        [StructLayout(LayoutKind.Sequential)]internal struct Iterator{internal IntPtr Data;internal int Rem,Index;}
        [DllImport(C,EntryPoint="xcb_connect")]internal static extern IntPtr Connect(IntPtr display,out int screen);
        [DllImport(C,EntryPoint="xcb_disconnect")]internal static extern void Disconnect(IntPtr c);
        [DllImport(C,EntryPoint="xcb_connection_has_error")]internal static extern int Error(IntPtr c);
        [DllImport(C,EntryPoint="xcb_get_setup")]internal static extern IntPtr Setup(IntPtr c);
        [DllImport(C,EntryPoint="xcb_setup_roots_iterator")]internal static extern Iterator Roots(IntPtr setup);
        [DllImport(C,EntryPoint="xcb_screen_next")]internal static extern void Next(ref Iterator iterator);
        [DllImport(C,EntryPoint="xcb_generate_id")]internal static extern uint Id(IntPtr c);
        [DllImport(C,EntryPoint="xcb_request_check")]internal static extern IntPtr Check(IntPtr c,Cookie cookie);
        [DllImport(F,EntryPoint="xcb_xfixes_query_version")]internal static extern Cookie Version(IntPtr c,uint major,uint minor);
        [DllImport(F,EntryPoint="xcb_xfixes_query_version_reply")]internal static extern IntPtr VersionReply(IntPtr c,Cookie cookie,out IntPtr error);
        [DllImport(F,EntryPoint="xcb_xfixes_create_region_checked")]internal static extern Cookie Region(IntPtr c,uint region,uint count,byte[] rectangles);
        [DllImport(F,EntryPoint="xcb_xfixes_destroy_region_checked")]internal static extern Cookie DestroyRegion(IntPtr c,uint region);
        [DllImport(F,EntryPoint="xcb_xfixes_set_window_shape_region_checked")]internal static extern Cookie Shape(IntPtr c,uint window,byte kind,short x,short y,uint region);
        [DllImport(F,EntryPoint="xcb_xfixes_hide_cursor_checked")]internal static extern Cookie HideCursor(IntPtr c,uint window);
        [DllImport(F,EntryPoint="xcb_xfixes_show_cursor_checked")]internal static extern Cookie ShowCursor(IntPtr c,uint window);
        [DllImport(C,EntryPoint="xcb_create_pixmap_checked")]internal static extern Cookie Pixmap(IntPtr c,byte depth,uint pixmap,uint drawable,ushort width,ushort height);
        [DllImport(C,EntryPoint="xcb_create_gc_checked")]internal static extern Cookie Gc(IntPtr c,uint gc,uint drawable,uint mask,uint[] values);
        [DllImport(C,EntryPoint="xcb_put_image_checked")]internal static extern Cookie Image(IntPtr c,byte format,uint drawable,uint gc,ushort width,ushort height,short x,short y,byte pad,byte depth,uint length,byte[] pixels);
        [DllImport(C,EntryPoint="xcb_create_window_checked")]internal static extern Cookie Window(IntPtr c,byte depth,uint window,uint parent,short x,short y,ushort width,ushort height,ushort border,ushort @class,uint visual,uint mask,uint[] values);
        [DllImport(C,EntryPoint="xcb_map_window_checked")]internal static extern Cookie Map(IntPtr c,uint window);
        [DllImport(C,EntryPoint="xcb_unmap_window_checked")]internal static extern Cookie Unmap(IntPtr c,uint window);
        [DllImport(C,EntryPoint="xcb_configure_window_checked")]internal static extern Cookie Configure(IntPtr c,uint window,ushort mask,uint[] values);
        [DllImport(C,EntryPoint="xcb_query_pointer")]internal static extern Cookie Pointer(IntPtr c,uint window);
        [DllImport(C,EntryPoint="xcb_query_pointer_reply")]internal static extern IntPtr PointerReply(IntPtr c,Cookie cookie,out IntPtr error);
        [DllImport(C,EntryPoint="xcb_setup_pixmap_formats_iterator")]internal static extern Iterator Formats(IntPtr setup);
        [DllImport(C,EntryPoint="xcb_format_next")]internal static extern void NextFormat(ref Iterator iterator);
        [DllImport(C,EntryPoint="xcb_screen_allowed_depths_iterator")]internal static extern Iterator Depths(IntPtr screen);
        [DllImport(C,EntryPoint="xcb_depth_next")]internal static extern void NextDepth(ref Iterator iterator);
        [DllImport(C,EntryPoint="xcb_depth_visuals_iterator")]internal static extern Iterator Visuals(IntPtr depth);
        [DllImport(C,EntryPoint="xcb_visualtype_next")]internal static extern void NextVisual(ref Iterator iterator);
        [DllImport("libc",EntryPoint="free")]internal static extern void Free(IntPtr p);
    }
}
