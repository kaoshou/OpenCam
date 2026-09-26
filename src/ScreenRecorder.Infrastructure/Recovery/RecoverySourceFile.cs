// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenRecorder.Infrastructure.Recovery;

internal static class RecoverySourceFile
{
    internal static FileStream FromUnixHandle(SafeFileHandle handle)
    {
        try { ValidateUnixHandle(handle); return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    internal static void ValidateUnixHandle(SafeFileHandle handle)
    {
        var valid = OperatingSystem.IsMacOS()
            ? fstat(handle, out var mac) == 0 && (mac.Mode & 0xF000) == 0x8000 && mac.Links == 1
            : statx(handle, "", 0x1000, 0x5, out var linux) == 0 && (linux.Mask & 5) == 5 && (linux.Mode & 0xF000) == 0x8000 && linux.Links == 1;
        if (!valid) throw new InvalidDataException("Session file must be regular and singly linked.");
    }
    // Validate the opened object, not only its name. Keep this handle until the
    // private snapshot is finished, so a later rename cannot redirect the copy.
    public static FileStream Open(string path)
    {
        SafeFileHandle handle;
        if (OperatingSystem.IsMacOS())
        {
            var fd = open(path, 0x100 | 0x4); // O_NOFOLLOW | O_NONBLOCK | O_RDONLY
            if (fd < 0) throw new IOException("Cannot safely open recovery source.");
            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            if (fstat(handle, out var info) != 0 || (info.Mode & 0xF000) != 0x8000 || info.Links != 1)
            {
                handle.Dispose();
                throw new InvalidDataException("Recovery source must be a regular, singly linked file.");
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid && Marshal.GetLastWin32Error() is 2 or 3)
            { handle.Dispose(); throw new FileNotFoundException("Session file does not exist.", path); }
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & (0x400u | 0x10u)) != 0 || info.Links != 1)
            {
                handle.Dispose();
                throw new InvalidDataException("Recovery source must be a regular, singly linked file.");
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux is also used by the repository's media-test CI job.
            var fd = open(path, 0x20000 | 0x800); // O_NOFOLLOW | O_NONBLOCK
            if (fd < 0) throw new IOException("Cannot safely open recovery source.");
            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            if (statx(handle, "", 0x1000, 0x5, out var info) != 0 ||
                (info.Mask & 0x5) != 0x5 || (info.Mode & 0xF000) != 0x8000 || info.Links != 1)
            {
                handle.Dispose();
                throw new InvalidDataException("Recovery source must be a regular, singly linked file.");
            }
        }
        else
        {
            throw new PlatformNotSupportedException("Safe recovery is supported on macOS and Windows.");
        }
        return new FileStream(handle, FileAccess.Read);
    }

    // Darwin's public 64-bit struct stat layout (arm64 and x86_64).
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacStat
    {
        [FieldOffset(4)] public ushort Mode;
        [FieldOffset(6)] public ushort Links;
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(SafeFileHandle handle, out MacStat stat);
    [DllImport("libc", SetLastError = true)]
    private static extern int statx(SafeFileHandle directory, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInfo info);
}
