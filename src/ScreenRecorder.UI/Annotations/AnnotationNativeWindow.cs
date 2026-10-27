// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ScreenRecorder.UI.Annotations;

/// <summary>Only native window/input policy. Does not alter cursor, capture exclusion or recorder state.</summary>
internal static class AnnotationNativeWindow
{
    private const string ObjC="/usr/lib/libobjc.A.dylib";
    public static void ConfigureToolbar(Window window)
    {
        if(OperatingSystem.IsWindows()&&window.TryGetPlatformHandle() is {HandleDescriptor:"HWND"} win)
            SetWindowPos(win.Handle,new nint(-1),0,0,0,0,0x1|0x2|0x10|0x4000);
        if(OperatingSystem.IsMacOS()&&window.TryGetPlatformHandle() is {HandleDescriptor:"NSWindow"} h)
            SendLong(h.Handle,Selector("setLevel:"),26);
    }
    public static void RaiseExistingPointer()
    {
        if(!OperatingSystem.IsWindows())return;
        var pointer=FindWindow("OpenCamCursorHighlightClass","OpenCamCursorHighlight");
        if(pointer!=0)SetWindowPos(pointer,new nint(-1),0,0,0,0,0x1|0x2|0x10|0x4000);
    }
    public static bool SetInteractive(Window window,bool interactive)
    {
        var handle=window.TryGetPlatformHandle();if(handle?.Handle is not { } h||h==0)return false;
        if(OperatingSystem.IsWindows())
        {
            const long transparent=0x20,noActivate=0x08000000,toolWindow=0x80;
            var flags=GetWindowLongPtr(h,-20).ToInt64();
            var next=interactive?(flags&~transparent&~noActivate)|toolWindow:flags|transparent|noActivate|toolWindow;
            SetWindowLongPtr(h,-20,(nint)next);
            return (GetWindowLongPtr(h,-20).ToInt64()&transparent)==(interactive?0:transparent);
        }
        if(OperatingSystem.IsMacOS())
        {
            // Avalonia.Native exposes an NSWindow pointer, as used by the existing transparency adapter.
            if(handle.HandleDescriptor!="NSWindow")return false;
            // Use the same explicit native transparency policy as the region selector.
            // Fail closed rather than leave an opaque window covering teaching content.
            if(!Views.MacOsWindowTransparency.TryApply(h))return false;
            SendBool(h,Selector("setIgnoresMouseEvents:"),!interactive);
            SendLong(h,Selector("setCollectionBehavior:"),(1<<0)|(1<<8)); // All Spaces + full-screen auxiliary.
            SendLong(h,Selector("setLevel:"),25); // Above ordinary application windows, below the existing teaching pointer.
            return GetBool(h,Selector("ignoresMouseEvents"))==!interactive;
        }
        return false;
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]private static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]private static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string className,string title);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport(ObjC,EntryPoint="sel_registerName")]private static extern nint Selector(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]private static extern void SendBool(nint receiver,nint selector,[MarshalAs(UnmanagedType.I1)]bool value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")][return:MarshalAs(UnmanagedType.I1)]private static extern bool GetBool(nint receiver,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]private static extern void SendLong(nint receiver,nint selector,long value);
}
