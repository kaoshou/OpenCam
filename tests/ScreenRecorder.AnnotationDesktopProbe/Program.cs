// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ScreenRecorder.Core.Annotations;
using ScreenRecorder.UI.Annotations;

namespace ScreenRecorder.AnnotationDesktopProbe;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(!OperatingSystem.IsWindows()){Console.WriteLine("SKIP: Windows desktop required");return 0;}
        return AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args,ShutdownMode.OnExplicitShutdown);
    }
}

public sealed class ProbeApp:Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var args=lifetime.Args??[];
        if(args.Length>0&&args[0]=="target")
        {
            var count=0;var file=args[1];
            var target=new Window{Title="OpenCam independent click target",Width=700,Height=500,Position=new(80,80),Topmost=true,
                WindowStartupLocation=WindowStartupLocation.Manual,SystemDecorations=SystemDecorations.None,Background=Brushes.White};
            var button=new Button{Background=Brushes.White,BorderThickness=new(0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Stretch};
            button.Click+=(_,_)=>File.WriteAllText(file,(++count).ToString());target.Content=button;
            Win32Properties.AddWndProcHookCallback(target,(nint h,uint message,nint w,nint l,ref bool handled)=>
            {
                if(message is 0x201 or 0x202)Console.WriteLine($"TARGET mouse message={message:x}, position={l:x}");
                return 0;
            });
            target.Show();target.UpdateLayout();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=(_,_)=>{if(File.Exists(file+".demote")){target.Topmost=false;File.Delete(file+".demote");}};timer.Start();
            Dispatcher.UIThread.Post(async()=>
            {
                await Task.Delay(500);target.UpdateLayout();
                Require(button.Bounds.Width>=target.ClientSize.Width-1&&button.Bounds.Height>=target.ClientSize.Height-1,"Target button does not fill its window");
                Console.WriteLine($"TARGET ready hwnd={target.TryGetPlatformHandle()!.Handle}, button={button.Bounds}, position={target.Position}");File.WriteAllText(file,"0");
            });return;
        }
        Dispatcher.UIThread.Post(async()=>
        {
            try{await RunProbe();lifetime.Shutdown(0);}
            catch(Exception error){Console.Error.WriteLine(error);lifetime.Shutdown(1);}
        });
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Until(Func<bool> condition,string message)
    {
        var timer=Stopwatch.StartNew();
        while(!condition()){if(timer.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException(message);await Task.Delay(50);}
    }
    private static async Task RunProbe()
    {
        var path=Path.Combine(Path.GetTempPath(),"opencam-annotation-clicks-"+Guid.NewGuid().ToString("N"));
        var childStart=new ProcessStartInfo("dotnet"){UseShellExecute=false};
        childStart.ArgumentList.Add(typeof(ProbeApp).Assembly.Location);childStart.ArgumentList.Add("target");childStart.ArgumentList.Add(path);
        using var child=Process.Start(childStart)??throw new InvalidOperationException("Cannot create independent target process");
        Native.GetCursorPos(out var saved);
        var owner=new Window{Title="OpenCam annotation desktop probe",Width=50,Height=50,Position=new(820,20),ShowActivated=false};
        AnnotationOverlayHost? host=null;
        try
        {
            await Until(()=>File.Exists(path),"Target process did not open its window");
            int Count(){try{return int.TryParse(File.ReadAllText(path),out var value)?value:-1;}catch(IOException){return -1;}}
            await Task.Delay(200);
            Console.WriteLine($"Baseline pixel={Native.Pixel(480,380):x}; foreground={Native.GetForegroundWindow()}; pointWindow={Native.WindowFromPoint(new(){X=480,Y=380})}");
            await Native.Click(new(480,380));await Until(()=>Count()==1,"Baseline independent target did not receive click");
            Console.WriteLine("PASS: baseline SendInput reaches independent target button");
            File.WriteAllText(path+".demote","");await Until(()=>!File.Exists(path+".demote"),"Target did not leave topmost state");
            owner.Show();var scale=owner.Screens.ScreenFromPoint(new(80,80))!.Scaling;
            host=new(owner,new(80,80,700,500),0xffe63946,8,(_,_)=>{},()=>true);
            host.Show();host.SelectTool(AnnotationTool.Pen);
            host.Document.Begin(new(360/scale,300/scale),true);host.Document.Move(new(440/scale,300/scale));host.Document.End();
            await Task.Delay(400);
            var surface=(Window)typeof(AnnotationOverlayHost).GetField("_overlay",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(host)!;
            var hwnd=surface.TryGetPlatformHandle()!.Handle;
            Require((Native.GetClassLongPtr(hwnd,-26).ToInt64()&0x20)!=0,"Probe did not use Avalonia's CS_OWNDC class");
            Require((Native.GetWindowLongPtr(hwnd,-20).ToInt64()&0x80000)==0,"Unsupported layered style on Avalonia HWND");
            for(var iteration=0;iteration<3;iteration++)
            {
                host.TogglePen(); // Operate with visible ink.
                await Task.Delay(200);
                var pixel=Native.Pixel(480,380);
                Require((pixel&255)>180&&((pixel>>8)&255)<120,"Ink disappeared in Operate mode: "+pixel.ToString("x"));
                foreach(var point in new[]{new PixelPoint(480,380),new PixelPoint(650,480)})
                {
                    var before=Count();Require(before>=0,"Target counter unavailable");await Native.Click(point);
                    await Until(()=>Count()==before+1,"Cross-process click did not reach target in Operate mode");
                    Require((Native.Pixel(480,380)&255)>180&&((Native.Pixel(480,380)>>8)&255)<120,"Ink disappeared after target activation");
                }
                host.SelectTool(AnnotationTool.Pen);
                var previous=Count();var marks=host.Document.Marks.Length;
                await Native.Click(new(650,480));
                await Until(()=>host.Document.Marks.Length==marks+1,"Drawing did not receive native input after re-enabling");
                Require(Count()==previous,"Drawing click leaked into underlying target");
            }
            host.TogglePen();host.Document.Clear();host.SelectTool(AnnotationTool.Remove);
            await Task.Delay(200);
            var cleared=Native.Pixel(480,380);
            Require((cleared&255)>220&&((cleared>>8)&255)>220&&((cleared>>16)&255)>220,"Cleared ink reappeared");
            host.SetPaused(true);host.SetPaused(false);
            Require(!child.HasExited,"Independent target exited unexpectedly");
            Console.WriteLine("PASS: actual Avalonia CS_OWNDC HWND, visible ink, independent-process SendInput at ink/blank, drawing input restored, clear/eraser and pause/resume");
        }
        finally
        {
            host?.Dispose();owner.Close();
            if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}
            File.Delete(path);File.Delete(path+".demote");Native.SetCursorPos(saved.X,saved.Y);
        }
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]internal struct Point{public int X,Y;}
        [StructLayout(LayoutKind.Explicit,Size=40)]private struct Input{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public Mouse Mouse;}
        [StructLayout(LayoutKind.Sequential)]private struct Mouse{public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
        internal static async Task Click(PixelPoint point)
        {
            Require(SetCursorPos(point.X,point.Y),"Cannot move test cursor");
            await Task.Delay(75);
            Require(SendInput(1,[new(){Mouse=new(){Flags=2}}],40)==1,"SendInput down was rejected");
            await Task.Delay(75);
            Require(SendInput(1,[new(){Mouse=new(){Flags=4}}],40)==1,"SendInput up was rejected");
        }
        internal static uint Pixel(int x,int y){var dc=GetDC(0);try{return GetPixel(dc,x,y);}finally{ReleaseDC(0,dc);}}
        [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")]internal static extern nint GetClassLongPtr(nint window,int index);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]internal static extern nint GetWindowLongPtr(nint window,int index);
        [DllImport("user32.dll")]private static extern uint SendInput(uint count,Input[] inputs,int size);
        [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")]internal static extern nint WindowFromPoint(Point point);
        [DllImport("user32.dll")]private static extern nint GetDC(nint window);
        [DllImport("user32.dll")]private static extern int ReleaseDC(nint window,nint dc);
        [DllImport("gdi32.dll")]private static extern uint GetPixel(nint dc,int x,int y);
    }
}
