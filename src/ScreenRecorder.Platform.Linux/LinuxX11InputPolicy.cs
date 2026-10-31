// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.Linux;

public static class LinuxX11InputPolicy
{
    public static bool IsX11=>OperatingSystem.IsLinux()&&ScreenRecorder.Media.Capture.ObsRuntime.DefaultBackend("linux-x64")=="x11";
    public static bool TrySetInteractive(nint window,bool interactive)
    {
        if(!IsX11||window<=0||(ulong)window>uint.MaxValue)return false;
        try{using var display=new LinuxX11Connection();display.Input((uint)window,interactive);return true;}
        catch(Exception error){Serilog.Log.Warning("Optional X11 annotation input policy unavailable: ErrorType={ErrorType}",error.GetType().Name);return false;}
    }
}
