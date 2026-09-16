using System.Runtime.InteropServices;

namespace ScreenRecorder.Platform.macOS;

internal static class UnixFifo
{
    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int mkfifo(string path, uint mode);

    internal static void CreatePrivate(string path)
    {
        const uint UserReadWrite = 0x180; // 0600
        if (mkfifo(path, UserReadWrite) != 0)
        {
            throw new IOException(
                $"mkfifo failed ({Marshal.GetLastWin32Error()}): {path}");
        }
    }
}
