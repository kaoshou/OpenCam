// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace ScreenRecorder.Platform.macOS;

/// <summary>Default endpoint IDs only; does not request microphone access or record audio.</summary>
public static class MacOsAudioEndpointIdentity
{
    // CoreAudio AudioHardware.h: system object1, default input 'dIn ', output 'dOut', global 'glob'.
    [StructLayout(LayoutKind.Sequential)]
    private struct Address { public uint Selector, Scope, Element; }
    [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
    private static extern int AudioObjectGetPropertyData(uint id, ref Address address, uint qualifierSize,
        IntPtr qualifier, ref uint size, out uint value);
    private static uint Read(uint selector)
    {
        var address = new Address { Selector = selector, Scope = 0x676c6f62, Element = 0 };
        uint size = 4;
        if (AudioObjectGetPropertyData(1, ref address, 0, IntPtr.Zero, ref size, out var value) != 0 || size != 4 || value == 0)
            throw new IOException("Audio endpoint unavailable.");
        return value;
    }
    public static string Read(bool microphone, bool system) =>
        (microphone ? Read(0x64496e20).ToString() : "disabled") + ":" + (system ? Read(0x644f7574).ToString() : "disabled");
}
