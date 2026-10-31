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
    public override void Initialize()=>Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
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
                    var bindings=await CheckConfiguredBindings();
                    var screen=window.Screens.ScreenFromWindow(window)??window.Screens.All.FirstOrDefault()??throw new IOException("fixture_screen_unavailable");
                    var target=button.PointToScreen(new(button.Bounds.Width/2,button.Bounds.Height/2));
                    var tx=target.X.ToString(System.Globalization.CultureInfo.InvariantCulture);var ty=target.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    host=new(window,new(screen.Bounds.X,screen.Bounds.Y,screen.Bounds.Width,screen.Bounds.Height),0xffe63946,4,(_,_)=>{},()=>true);
                    host.Show();host.SelectTool(AnnotationTool.Pen);await Task.Delay(250);
                    await Input("mousemove",tx,ty,"mousedown","1","mousemove",(target.X+50).ToString(),(target.Y+30).ToString(),"mouseup","1");await Task.Delay(200);
                    if(host.Document.Marks.Length!=1||clicked!=0)throw new IOException("native_drawing_input_failed");
                    host.TogglePen();await Input("mousemove",tx,ty,"click","1");await Task.Delay(100);
                    if(clicked!=1||host.Document.Marks.Length!=1) {
                        using var inspector=new X11Inspector();
                        await File.WriteAllTextAsync(Path.Combine(Evidence,"annotation-windows.json"),JsonSerializer.Serialize(new{child=inspector.Child(),roots=inspector.Windows(),clients=desktop.Windows.Select(w=>new{window=w.Title,id=(long)(w.TryGetPlatformHandle()?.Handle??0)})}));
                        throw new IOException($"native_click_through_failed_clicks_{clicked}_marks_{host.Document.Marks.Length}_origin_{window.PointToScreen(default).X}_{window.PointToScreen(default).Y}");
                    }
                    for(var i=0;i<6;i++){host.ToggleToolbar();host.ToggleToolbar();}
                    host.SelectTool(AnnotationTool.Rectangle);await Input("mousemove","330","410","mousedown","1","mousemove","400","450","mouseup","1");await Task.Delay(100);
                    if(host.Document.Marks.Length!=2)throw new IOException("native_rectangle_failed");
                    host.FinishDrawing();if(!host.Document.Marks.IsEmpty)throw new IOException("finish_did_not_clear");
                    await Input("mousemove",tx,ty,"click","1");await Task.Delay(100);if(clicked!=2)throw new IOException("finish_did_not_release_input");
                    if(!bindings)throw new IOException("configured_lifecycle_hotkeys_missing");
                    await File.WriteAllTextAsync(Path.Combine(Evidence,"annotation-result.json"),JsonSerializer.Serialize(new{status="PASS",scope="actual Avalonia X11/compositor drawing, click-through, toolbar hide/show and finish/clear; no physical multimonitor claim",handle=window.TryGetPlatformHandle()?.HandleDescriptor}));
                    host.Dispose();desktop.Shutdown(0);
                }catch(Exception e) {
                    var transparency=desktop.Windows.Select(w=>new{window=w.Title,level=w.ActualTransparencyLevel.ToString()}).ToArray();
                    host?.Dispose();await File.WriteAllTextAsync(Path.Combine(Evidence,"annotation-result.json"),JsonSerializer.Serialize(new{status="FAIL",errorType=e.GetType().Name,message=e is IOException?e.Message:e.Message=="Native annotation input policy unavailable"?"native_annotation_input_unavailable":"annotation_fixture_failed",handle=window.TryGetPlatformHandle()?.HandleDescriptor,transparency}));desktop.Shutdown(1);
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
    private static async Task<bool> CheckConfiguredBindings()
    {
        var vm=new ScreenRecorder.UI.ViewModels.MainViewModel(forScreenshot:true);
        using var native=new ScreenRecorder.Platform.Linux.LinuxX11HotkeyService();
        using var conflict=new ScreenRecorder.Platform.Linux.LinuxX11HotkeyService();
        typeof(ScreenRecorder.UI.ViewModels.MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(vm,native);
        bool success;
        try {
            var error=vm.TryApplyHotkeys(new ScreenRecorder.Core.Models.UserSettings());
            success=error is null&&vm.IsRecordingHotkeyRegistered&&!conflict.RegisterHotkey(9001,0x7B,2)&&!conflict.RegisterHotkey(9002,0x7B,0);
        }finally{vm.Cleanup();}
        await File.WriteAllTextAsync(Path.Combine(Evidence,"settings-hotkeys-result.json"),JsonSerializer.Serialize(new{status=success?"PASS":"FAIL",scope="actual MainViewModel settings registration and checked native collision on Ctrl+F12/F12"}));return success;
    }
}
