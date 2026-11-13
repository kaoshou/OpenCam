// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.UI.Annotations;

namespace ScreenRecorder.Media.Tests;

public sealed class AnnotationWindowsInputTests
{
    [WindowsAnnotationFact]
    public void NativePolicySupportsRepeatedOperateDrawTransitionsWithoutHidingWindowContent()
    {
        var window=CreateWindowEx(0,"STATIC","OpenCam annotation input test",0x80000000,0,0,200,150,0,0,0,0);
        Assert.NotEqual(0,window);
        try
        {
            for(var i=0;i<10;i++)
            {
                Assert.True(AnnotationNativeWindow.SetWindowsInteractive(window,false));
                Assert.Equal(0x080800a0L,GetWindowLongPtr(window,-20).ToInt64()&0x080800a0);
                Assert.True(GetLayeredWindowAttributes(window,out _,out var alpha,out var flags));
                Assert.Equal(255,alpha);Assert.Equal(2u,flags);
                Assert.True(AnnotationNativeWindow.SetWindowsInteractive(window,true));
                Assert.Equal(0x80L,GetWindowLongPtr(window,-20).ToInt64()&0x080800a0);
            }
        }
        finally{DestroyWindow(window);}
    }
    [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetLayeredWindowAttributes(nint window,out uint color,out byte alpha,out uint flags);
}

internal sealed class WindowsAnnotationFactAttribute:FactAttribute
{
    public WindowsAnnotationFactAttribute()
    {
        if(!OperatingSystem.IsWindows())Skip="Requires Windows user32; desktop click-through and ink visibility still require manual validation.";
    }
}
