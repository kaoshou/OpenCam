namespace ScreenRecorder.Media.FFmpeg;

public static class FFmpegDiscovery
{
    public static string? FindFFmpegExecutable()
    {
        return FindOnPath("ffmpeg") ?? FindInAppDirectory("ffmpeg");
    }

    public static string? FindFFprobeExecutable()
    {
        return FindOnPath("ffprobe") ?? FindInAppDirectory("ffprobe");
    }

    private static string? FindInAppDirectory(string exeName)
    {
        var exe = OperatingSystem.IsWindows() ? $"{exeName}.exe" : exeName;
        
        // Use ProcessPath to reliably find the directory of the single-file executable
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        var fullPath = Path.Combine(appDir, exe);
        if (File.Exists(fullPath)) return fullPath;

        // Fallback for .NET single-file extraction temp directory just in case
        var tempDir = AppDomain.CurrentDomain.BaseDirectory;
        var fullPathTemp = Path.Combine(tempDir, exe);
        if (File.Exists(fullPathTemp)) return fullPathTemp;

        var runtimesDir = Path.Combine(tempDir, "runtimes", "win-x64", "native", exe);
        if (File.Exists(runtimesDir)) return runtimesDir;

        return null;
    }

    private static string? FindOnPath(string exeName)
    {
        var exe = OperatingSystem.IsWindows() ? $"{exeName}.exe" : exeName;
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var path in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(path, exe);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }
}
