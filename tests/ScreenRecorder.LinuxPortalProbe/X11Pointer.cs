// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Models;
using System.Text.Json;
internal static class X11Pointer
{
    public static async Task<int> RunAsync(string evidence)
    {
        if(!OperatingSystem.IsLinux()||Environment.GetEnvironmentVariable("OPENCAM_DISPOSABLE_CI_DESKTOP")!="1")return 2;
        Directory.CreateDirectory(evidence);
        try {
            // Hold a client connection before XTEST exits; otherwise Xvfb resets
            // its pointer to the centre when its final client disconnects.
            using var inspector=new X11Inspector();
            using(var move=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdotool"){ArgumentList={"mousemove","120","140"},UseShellExecute=false})!)await move.WaitForExitAsync();
            var art=TeachingPointerArtwork.Get(new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large));
            var x=(short)(120-(int)Math.Round(art.HotspotX));var y=(short)(140-(int)Math.Round(art.HotspotY));
            var before=inspector.Read(x,y,(ushort)art.Width,(ushort)art.Height);var child=inspector.Child();
            using var pointer=new LinuxX11PointerOverlay();
            if(!await pointer.StartPresentationAsync(new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large))||!pointer.IsRunning)
                throw new IOException("pointer_not_ready");
            await Task.Delay(500);
            await File.WriteAllTextAsync(Path.Combine(evidence,"pointer-windows.json"),JsonSerializer.Serialize(inspector.Windows()));
            if(!pointer.IsRunning)throw new IOException("pointer_lost_after_ready");
            var after=inspector.Read(x,y,(ushort)art.Width,(ushort)art.Height);var original=art.Pixels.ToArray();
            for(var i=0;i<original.Length;i+=4) {
                var expected=original[i+3]>=128?new byte[]{original[i+2],original[i+1],original[i]}:before.AsSpan(i,3).ToArray();
                if(!after.AsSpan(i,3).SequenceEqual(expected)) {
                    await File.WriteAllBytesAsync(Path.Combine(evidence,"pointer-before.bgra"),before);
                    await File.WriteAllBytesAsync(Path.Combine(evidence,"pointer-after.bgra"),after);
                    throw new IOException($"pointer_artwork_or_hotspot_mismatch_pixel_{i/4}_alpha_{original[i+3]}_expected_{Convert.ToHexString(expected)}_actual_{Convert.ToHexString(after.AsSpan(i,3))}");
                }
            }
            if(inspector.Child()!=child)throw new IOException("pointer_intercepts_input");
            pointer.Stop();if(pointer.IsRunning)throw new IOException("pointer_not_stopped");
            if(!inspector.Read(x,y,(ushort)art.Width,(ushort)art.Height).SequenceEqual(before))throw new IOException("pointer_not_removed");
            if(!await pointer.StartPresentationAsync(TeachingPointerOptions.Default))throw new IOException("pointer_restart_failed");
            pointer.Stop();
            var target=inspector.Window();
            if(inspector.Child()!=target)throw new IOException("fixture_window_hit_failed");
            for(var attempt=0;attempt<5;attempt++) {
                if(!LinuxX11InputPolicy.TrySetInteractive((nint)target,false)||inspector.Child()==target)throw new IOException("input_not_released");
                if(!LinuxX11InputPolicy.TrySetInteractive((nint)target,true)||inspector.Child()!=target)throw new IOException("drawing_input_not_restored");
            }
            if(LinuxX11InputPolicy.TrySetInteractive((nint)uint.MaxValue,false))throw new IOException("invalid_window_accepted");
            await File.WriteAllTextAsync(Path.Combine(evidence,"pointer-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="actual X11 pixels, original artwork, hotspot, input transparency and teardown/restart; not physical multimonitor or video acceptance"}));return 0;
        }catch(Exception e){await File.WriteAllTextAsync(Path.Combine(evidence,"pointer-result.json"),JsonSerializer.Serialize(new{status="FAIL",errorType=e.GetType().Name,code=e is IOException?e.Message:"fixture_failed"}));return 1;}
    }
}
