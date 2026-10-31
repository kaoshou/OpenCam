// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
internal sealed class X11Inspector:IDisposable
{
    const string L="libxcb.so.1";
    internal readonly IntPtr Connection;
    internal readonly uint Root;
    public X11Inspector(){Connection=Connect(IntPtr.Zero,out _);var screens=Roots(Setup(Connection));Root=(uint)Marshal.ReadInt32(screens.Data);}
    internal byte[] Read(short x,short y,ushort width,ushort height)
    {
        var reply=ImageReply(Connection,Image(Connection,2,Root,x,y,width,height,uint.MaxValue),out var error);
        if(error!=IntPtr.Zero)Free(error);
        if(reply==IntPtr.Zero||error!=IntPtr.Zero)throw new IOException("fixture_image_failed");
        try{var size=DataLength(reply);if(size!=width*height*4)throw new IOException("fixture_image_layout");var bytes=new byte[size];Marshal.Copy(Data(reply),bytes,0,size);return bytes;}
        finally{Free(reply);}
    }
    internal uint Child()
    {
        var reply=PointerReply(Connection,Pointer(Connection,Root),out var error);if(error!=IntPtr.Zero)Free(error);
        if(reply==IntPtr.Zero||error!=IntPtr.Zero)throw new IOException("fixture_pointer_failed");
        try{return(uint)Marshal.ReadInt32(reply,12);}finally{Free(reply);}
    }
    internal uint Window()
    {
        var window=Id(Connection);
        var error=Check(Connection,Create(Connection,0,window,Root,100,100,200,200,0,1,0,0,[]));
        if(error!=IntPtr.Zero){Free(error);throw new IOException("fixture_window_failed");}
        error=Check(Connection,Map(Connection,window));if(error!=IntPtr.Zero){Free(error);throw new IOException("fixture_map_failed");}return window;
    }
    internal object[] Windows()
    {
        var tree=TreeReply(Connection,Tree(Connection,Root),out var error);if(error!=IntPtr.Zero)Free(error);
        if(tree==IntPtr.Zero||error!=IntPtr.Zero)throw new IOException("fixture_tree_failed");
        try {
            var result=new List<object>();var data=Children(tree);
            for(var i=0;i<ChildrenLength(tree);i++) {
                var id=(uint)Marshal.ReadInt32(data,i*4);var geometry=GeometryReply(Connection,Geometry(Connection,id),out error);
                if(error!=IntPtr.Zero)Free(error);if(geometry==IntPtr.Zero)continue;
                try{result.Add(new{id,x=Marshal.ReadInt16(geometry,12),y=Marshal.ReadInt16(geometry,14),width=Marshal.ReadInt16(geometry,16),height=Marshal.ReadInt16(geometry,18)});}finally{Free(geometry);}
            }return result.ToArray();
        }finally{Free(tree);}
    }
    public void Dispose()=>Disconnect(Connection);
    [StructLayout(LayoutKind.Sequential)]struct Cookie{internal uint Sequence;}
    [StructLayout(LayoutKind.Sequential)]struct Iterator{internal IntPtr Data;internal int Rem,Index;}
    [DllImport(L,EntryPoint="xcb_connect")]static extern IntPtr Connect(IntPtr display,out int screen);
    [DllImport(L,EntryPoint="xcb_disconnect")]static extern void Disconnect(IntPtr c);
    [DllImport(L,EntryPoint="xcb_get_setup")]static extern IntPtr Setup(IntPtr c);
    [DllImport(L,EntryPoint="xcb_setup_roots_iterator")]static extern Iterator Roots(IntPtr setup);
    [DllImport(L,EntryPoint="xcb_get_image")]static extern Cookie Image(IntPtr c,byte format,uint drawable,short x,short y,ushort width,ushort height,uint mask);
    [DllImport(L,EntryPoint="xcb_get_image_reply")]static extern IntPtr ImageReply(IntPtr c,Cookie cookie,out IntPtr error);
    [DllImport(L,EntryPoint="xcb_get_image_data")]static extern IntPtr Data(IntPtr reply);
    [DllImport(L,EntryPoint="xcb_get_image_data_length")]static extern int DataLength(IntPtr reply);
    [DllImport(L,EntryPoint="xcb_query_pointer")]static extern Cookie Pointer(IntPtr c,uint window);
    [DllImport(L,EntryPoint="xcb_query_pointer_reply")]static extern IntPtr PointerReply(IntPtr c,Cookie cookie,out IntPtr error);
    [DllImport(L,EntryPoint="xcb_generate_id")]static extern uint Id(IntPtr c);
    [DllImport(L,EntryPoint="xcb_request_check")]static extern IntPtr Check(IntPtr c,Cookie cookie);
    [DllImport(L,EntryPoint="xcb_create_window_checked")]static extern Cookie Create(IntPtr c,byte depth,uint window,uint parent,short x,short y,ushort width,ushort height,ushort border,ushort kind,uint visual,uint mask,uint[] values);
    [DllImport(L,EntryPoint="xcb_map_window_checked")]static extern Cookie Map(IntPtr c,uint window);
    [DllImport(L,EntryPoint="xcb_query_tree")]static extern Cookie Tree(IntPtr c,uint window);
    [DllImport(L,EntryPoint="xcb_query_tree_reply")]static extern IntPtr TreeReply(IntPtr c,Cookie cookie,out IntPtr error);
    [DllImport(L,EntryPoint="xcb_query_tree_children")]static extern IntPtr Children(IntPtr reply);
    [DllImport(L,EntryPoint="xcb_query_tree_children_length")]static extern int ChildrenLength(IntPtr reply);
    [DllImport(L,EntryPoint="xcb_get_geometry")]static extern Cookie Geometry(IntPtr c,uint window);
    [DllImport(L,EntryPoint="xcb_get_geometry_reply")]static extern IntPtr GeometryReply(IntPtr c,Cookie cookie,out IntPtr error);
    [DllImport("libc",EntryPoint="free")]static extern void Free(IntPtr p);
}
