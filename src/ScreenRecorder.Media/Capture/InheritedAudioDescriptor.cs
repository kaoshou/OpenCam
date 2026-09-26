// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenRecorder.Media.Capture;

internal sealed class InheritedAudioDescriptor : IDisposable
{
    private readonly SafeFileHandle _handle;
    private InheritedAudioDescriptor(SafeFileHandle handle) => _handle = handle;
    public string Input(int channels) => $"-thread_queue_size 1024 -f s16le -ar 48000 -ac {channels} -i pipe:{_handle.DangerousGetHandle().ToInt32()} ";

    public static InheritedAudioDescriptor? Duplicate(Stream? stream)
    {
        if (stream == null) return null;
        if (!OperatingSystem.IsMacOS() || stream is not PipeStream pipe)
            throw new NotSupportedException("Native PCM requires an anonymous macOS pipe.");
        // dup clears FD_CLOEXEC on the duplicate only. The capture-owned original
        // survives FFmpeg encoder fallback. Dispose duplicates immediately after spawn.
        var fd = dup(pipe.SafePipeHandle);
        if (fd < 0) throw new IOException("Cannot inherit audio pipe.");
        return new InheritedAudioDescriptor(new SafeFileHandle((IntPtr)fd, true));
    }
    public void Dispose() => _handle.Dispose();
    [DllImport("libc", SetLastError = true)] private static extern int dup(SafePipeHandle handle);
}
