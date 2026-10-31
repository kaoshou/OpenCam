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
            using var pointer=new LinuxX11PointerOverlay();
            if(!await pointer.StartPresentationAsync(new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large))||!pointer.IsRunning)
                throw new IOException("pointer_not_ready");
            await Task.Delay(500);
            pointer.Stop();if(pointer.IsRunning)throw new IOException("pointer_not_stopped");
            if(!await pointer.StartPresentationAsync(TeachingPointerOptions.Default))throw new IOException("pointer_restart_failed");
            pointer.Stop();
            await File.WriteAllTextAsync(Path.Combine(evidence,"pointer-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="X11 pointer native startup/stop/restart only; content and input validation pending"}));return 0;
        }catch(Exception e){await File.WriteAllTextAsync(Path.Combine(evidence,"pointer-result.json"),JsonSerializer.Serialize(new{status="FAIL",errorType=e.GetType().Name,code=e is IOException?e.Message:"fixture_failed"}));return 1;}
    }
}
