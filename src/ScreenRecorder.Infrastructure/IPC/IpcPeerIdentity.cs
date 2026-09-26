// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenRecorder.Infrastructure.IPC;

public static class IpcPeerIdentity
{
    public static void Verify(PipeStream pipe, int expectedPid, bool server)
    {
        if (expectedPid <= 0) throw new InvalidDataException("Missing IPC peer identity.");
        int pid;
        if (OperatingSystem.IsWindows())
        {
            uint result;
            var ok = server
                ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out result)
                : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out result);
            if (!ok) throw new IOException("Cannot identify IPC peer.");
            pid = checked((int)result);
        }
        else if (OperatingSystem.IsMacOS())
        {
            uint length = sizeof(int);
            if (getsockopt(pipe.SafePipeHandle, 0, 2, out pid, ref length) != 0)
                throw new IOException("Cannot identify macOS IPC peer.");
        }
        else
        {
            uint length = 12;
            if (GetPeerCredentials(pipe.SafePipeHandle, 1, 17, out var credentials, ref length) != 0)
                throw new IOException("Cannot identify Unix IPC peer.");
            pid = credentials.Pid;
        }
        if (pid != expectedPid) throw new InvalidDataException("Unexpected IPC peer process.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(SafePipeHandle socket, int level, int option, out int value, ref uint length);
    [StructLayout(LayoutKind.Sequential)]
    private struct Credentials { public int Pid; public uint Uid; public uint Gid; }
    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetPeerCredentials(SafePipeHandle socket, int level, int option, out Credentials value, ref uint length);
}
