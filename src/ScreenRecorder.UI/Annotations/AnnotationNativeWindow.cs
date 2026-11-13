// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ScreenRecorder.UI.Annotations;

/// <summary>Only native window/input policy. Does not alter cursor, capture exclusion or recorder state.</summary>
internal static class AnnotationNativeWindow
{
    private const string ObjC="/usr/lib/libobjc.A.dylib";
    public static void ConfigureOverlay(Window window,Func<bool> interactive)
    {
        if(!OperatingSystem.IsWindows())return;
        // Avalonia recomputes extended styles on window property changes. Keep
        // our input policy in that calculation, including after Show/Activate.
        Win32Properties.AddWindowStylesCallback(window,(style,exStyle)=>(style,(uint)WindowsInputStyles(exStyle,interactive())));
    }
    public static void ConfigureToolbar(Window window)
    {
        if(OperatingSystem.IsWindows()&&window.TryGetPlatformHandle() is {HandleDescriptor:"HWND"} win)
            SetWindowPos(win.Handle,new nint(-1),0,0,0,0,0x1|0x2|0x10);
        if(OperatingSystem.IsMacOS()&&window.TryGetPlatformHandle() is {HandleDescriptor:"NSWindow"} h)
            SendLong(h.Handle,Selector("setLevel:"),26);
        if(ScreenRecorder.Platform.Linux.LinuxX11InputPolicy.IsX11&&window.TryGetPlatformHandle() is {HandleDescriptor:"XID"} x11)
            ScreenRecorder.Platform.Linux.LinuxX11InputPolicy.TryRaise(x11.Handle);
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
            return SetWindowsInteractive(h,interactive);
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
        if(ScreenRecorder.Platform.Linux.LinuxX11InputPolicy.IsX11&&handle.HandleDescriptor=="XID")
        {
            // A compositor is mandatory for the full-screen annotation surface.
            // Never leave an opaque or input-blocking fallback over the desktop.
            if(window.ActualTransparencyLevel!=WindowTransparencyLevel.Transparent)return false;
            return ScreenRecorder.Platform.Linux.LinuxX11InputPolicy.TrySetInteractive(h,interactive);
        }
        return false;
    }
    internal static long WindowsInputStyles(long flags,bool interactive)
    {
        const long transparent=0x20,layered=0x80000,noActivate=0x08000000,toolWindow=0x80;
        // TRANSPARENT alone specifies paint order, not cross-process input
        // pass-through. LAYERED + TRANSPARENT ignores even opaque ink pixels.
        // Drawing removes LAYERED so blank canvas pixels receive mouse input.
        return interactive?(flags&~transparent&~layered&~noActivate)|toolWindow:flags|transparent|layered|noActivate|toolWindow;
    }
    internal static bool SetWindowsInteractive(nint window,bool interactive)
    {
        var next=WindowsInputStyles(GetWindowLongPtr(window,-20).ToInt64(),interactive);
        SetWindowLongPtr(window,-20,(nint)next);
        // Initialize the layered window at full opacity; per-pixel composition
        // still comes from Avalonia. No annotation or recording pixels are hidden.
        if(!interactive&&!SetLayeredWindowAttributes(window,0,255,2))return false;
        if(!SetWindowPos(window,0,0,0,0,0,0x1|0x2|0x4|0x10|0x20))return false;
        const long policy=0x080800a0;
        return (GetWindowLongPtr(window,-20).ToInt64()&policy)==(next&policy);
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]private static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]private static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string className,string title);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetLayeredWindowAttributes(nint window,uint color,byte alpha,uint flags);
    [DllImport(ObjC,EntryPoint="sel_registerName")]private static extern nint Selector(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]private static extern void SendBool(nint receiver,nint selector,[MarshalAs(UnmanagedType.I1)]bool value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")][return:MarshalAs(UnmanagedType.I1)]private static extern bool GetBool(nint receiver,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]private static extern void SendLong(nint receiver,nint selector,long value);
}
