// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ScreenRecorder.UI.Annotations;

/// <summary>Input-free ink presentation. Never changes the Avalonia renderer or recording pipeline.</summary>
internal sealed class WindowsAnnotationPresentation:IDisposable
{
    private nint _window;
    public void Update(AnnotationCanvas canvas,PixelSize size,PixelPoint origin,double scale)
    {
        if(canvas.Document.Marks.IsEmpty){Hide();return;}
        if(_window==0)
        {
            // STATIC has no CS_OWNDC. Unlike Avalonia's drawing HWND, this
            // presentation-only window supports LAYERED + TRANSPARENT.
            _window=CreateWindowEx(0x080800a8,"STATIC","OpenCam Annotation Presentation",0x80000000,origin.X,origin.Y,size.Width,size.Height,0,0,0,0);
            if(_window==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        canvas.UpdateLayout();
        using var image=new RenderTargetBitmap(size,new Vector(96*scale,96*scale));
        image.Render(canvas);
        nint screen=0,memory=0,bitmap=0,previous=0;
        try
        {
            screen=GetDC(0);memory=CreateCompatibleDC(screen);
            if(screen==0||memory==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            var info=new BitmapInfo{Size=40,Width=size.Width,Height=-size.Height,Planes=1,Bits=32};
            bitmap=CreateDIBSection(memory,ref info,0,out var pixels,0,0);
            if(bitmap==0||pixels==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            previous=SelectObject(memory,bitmap);
            if(previous==0||previous==new nint(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
            using var buffer=new Framebuffer(pixels,size,new(96*scale,96*scale));
            image.CopyPixels(buffer,AlphaFormat.Premul);
            var position=new Point{X=origin.X,Y=origin.Y};var dimensions=new Size{Width=size.Width,Height=size.Height};
            var source=new Point();var blend=new Blend{Alpha=255,Format=1};
            if(!UpdateLayeredWindow(_window,screen,ref position,ref dimensions,memory,ref source,0,ref blend,2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            ShowWindow(_window,4); // SW_SHOWNOACTIVATE, only after replacing all pixels.
        }
        finally
        {
            if(previous!=0&&previous!=new nint(-1))SelectObject(memory,previous);
            if(bitmap!=0)DeleteObject(bitmap);
            if(memory!=0)DeleteDC(memory);
            if(screen!=0)ReleaseDC(0,screen);
        }
    }
    public void Hide(){if(_window!=0)ShowWindow(_window,0);}
    public void Dispose(){if(_window!=0){DestroyWindow(_window);_window=0;}}
    private sealed class Framebuffer(nint address,PixelSize size,Vector dpi):ILockedFramebuffer
    {
        public nint Address=>address;
        public PixelSize Size=>size;
        public int RowBytes=>checked(size.Width*4);
        public Vector Dpi=>dpi;
        public PixelFormat Format=>PixelFormat.Bgra8888;
        public void Dispose(){}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Size{public int Width,Height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend{public byte Operation,Flags,Alpha,Format;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo
    {public int Size,Width,Height;public short Planes,Bits;public int Compression,ImageSize,XPels,YPels,Used,Important;}
    [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]private static extern nint GetDC(nint window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(nint window,nint dc);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint dc,nint item);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint pixels,nint section,uint offset);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool UpdateLayeredWindow(nint window,nint dest,ref Point position,ref Size size,nint source,ref Point sourcePosition,uint key,ref Blend blend,uint flags);
}
