using Avalonia;
using System;

namespace ScreenRecorder.UI;

internal class Program
{
    // 初始化程式碼，勿使用任何 Avalonia、第三方或包含 UI 程式碼的命名空間
    [STAThread]
    public static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--daemon") >= 0)
        {
            ScreenRecorder.Recorder.Program.Main(args).GetAwaiter().GetResult();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia 設定，請勿移除；設計工具需要此方法。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
