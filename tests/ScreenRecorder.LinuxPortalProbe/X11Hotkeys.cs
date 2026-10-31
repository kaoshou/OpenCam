// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using System.Diagnostics;
using System.Text.Json;
internal static class X11Hotkeys
{
    public static async Task<int> RunAsync(string evidence)
    {
        if(!OperatingSystem.IsLinux()||Environment.GetEnvironmentVariable("OPENCAM_DISPOSABLE_CI_DESKTOP")!="1")return 2;
        Directory.CreateDirectory(evidence);
        try {
            using var first=new LinuxX11HotkeyService();using var second=new LinuxX11HotkeyService();
            int f12=0,ctrl=0;
            first.HotkeyTriggered+=(_,id)=>{if(id==9001)Interlocked.Increment(ref f12);if(id==9002)Interlocked.Increment(ref ctrl);};
            if(!first.RegisterHotkey(9001,0x7B,0)||second.RegisterHotkey(9001,0x7B,0))throw new IOException("grab_collision");
            async Task Key(params string[] args) {
                var start=new ProcessStartInfo("xdotool"){UseShellExecute=false};foreach(var arg in args)start.ArgumentList.Add(arg);
                using var process=Process.Start(start)??throw new IOException("fixture_input_start");
                await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("fixture_input_failed");
            }
            await Key("keydown","F12");await Task.Delay(1400);await Key("keyup","F12");await Task.Delay(100);
            if(f12!=1)throw new IOException("autorepeat_or_missing_trigger");
            if(!first.RegisterHotkey(9002,0x7B,2))throw new IOException("modifier_grab");
            await Key("key","ctrl+F12");await Task.Delay(100);
            if(ctrl!=1||f12!=1)throw new IOException("modifier_trigger");
            await Key("key","Caps_Lock","F12","Caps_Lock");await Task.Delay(100);
            if(f12!=2)throw new IOException("lock_modifier_trigger");
            first.UnregisterHotkey(9001);
            if(!second.RegisterHotkey(9001,0x7B,0))throw new IOException("ungrab_failed");
            second.Dispose();
            if(!first.RegisterHotkey(9001,0x7B,0))throw new IOException("dispose_did_not_ungrab");
            await File.WriteAllTextAsync(Path.Combine(evidence,"hotkeys-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="actual X11 passive grabs and disposable XTEST input; not physical keyboard",f12,ctrl}));
            return 0;
        }catch(Exception e){await File.WriteAllTextAsync(Path.Combine(evidence,"hotkeys-result.json"),JsonSerializer.Serialize(new{status="FAIL",errorType=e.GetType().Name,code=e is IOException?e.Message:"fixture_failed"}));return 1;}
    }
}
