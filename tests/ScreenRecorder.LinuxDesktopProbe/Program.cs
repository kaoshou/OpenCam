// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using ScreenRecorder.UI.Annotations;
using ScreenRecorder.Core.Annotations;
using System.Diagnostics;
using System.Text.Json;

if(!OperatingSystem.IsLinux()||Environment.GetEnvironmentVariable("OPENCAM_DISPOSABLE_CI_DESKTOP")!="1"||args.Length!=1)return 2;
DesktopProbe.Evidence=Path.GetFullPath(args[0]);Directory.CreateDirectory(DesktopProbe.Evidence);
return AppBuilder.Configure<DesktopProbe>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);

internal sealed class DesktopProbe:Application
{
    internal static string Evidence="";
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var clicked=0;var button=new Button{Content="Disposable X11 input target",HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Center};
            button.Click+=(_,_)=>clicked++;
            var window=new Window{Title="OpenCam X11 annotation acceptance",Width=300,Height=200,Position=new(200,300),WindowStartupLocation=WindowStartupLocation.Manual,
                Content=button,Background=Brushes.White};
            desktop.MainWindow=window;
            window.Opened+=async(_,_)=> {
                AnnotationOverlayHost? host=null;
                try {
                    await Task.Delay(500);
                    var screen=window.Screens.Primary??throw new IOException("fixture_screen_unavailable");
                    host=new(window,new(screen.Bounds.X,screen.Bounds.Y,screen.Bounds.Width,screen.Bounds.Height),0xffe63946,4,(_,_)=>{},()=>true);
                    host.Show();host.SelectTool(AnnotationTool.Pen);await Task.Delay(250);
                    await Input("mousemove","250","350","mousedown","1","mousemove","300","380","mouseup","1");await Task.Delay(200);
                    if(host.Document.Marks.Length!=1||clicked!=0)throw new IOException("native_drawing_input_failed");
                    host.TogglePen();await Input("mousemove","250","350","click","1");await Task.Delay(100);
                    if(clicked!=1||host.Document.Marks.Length!=1)throw new IOException("native_click_through_failed");
                    for(var i=0;i<6;i++){host.ToggleToolbar();host.ToggleToolbar();}
                    host.SelectTool(AnnotationTool.Rectangle);await Input("mousemove","330","410","mousedown","1","mousemove","400","450","mouseup","1");await Task.Delay(100);
                    if(host.Document.Marks.Length!=2)throw new IOException("native_rectangle_failed");
                    host.FinishDrawing();if(!host.Document.Marks.IsEmpty)throw new IOException("finish_did_not_clear");
                    await Input("mousemove","250","350","click","1");await Task.Delay(100);if(clicked!=2)throw new IOException("finish_did_not_release_input");
                    await File.WriteAllTextAsync(Path.Combine(Evidence,"annotation-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="actual Avalonia X11/compositor drawing, click-through, toolbar hide/show and finish/clear; no physical multimonitor claim",handle=window.TryGetPlatformHandle()?.HandleDescriptor}));
                    host.Dispose();desktop.Shutdown(0);
                }catch(Exception e) {
                    host?.Dispose();await File.WriteAllTextAsync(Path.Combine(Evidence,"annotation-result.json"),JsonSerializer.Serialize(new{status="FAIL",errorType=e.GetType().Name,message=e is IOException?e.Message:"annotation_fixture_failed",handle=window.TryGetPlatformHandle()?.HandleDescriptor}));desktop.Shutdown(1);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static async Task Input(params string[] args)
    {
        var start=new ProcessStartInfo("xdotool"){UseShellExecute=false};foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("fixture_input_failed");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("fixture_input_failed");
    }
}
