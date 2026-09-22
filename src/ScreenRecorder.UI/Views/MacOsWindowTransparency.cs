// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace ScreenRecorder.UI.Views;

internal static class MacOsWindowTransparency
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendObject(
        IntPtr receiver,
        IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendObject(
        IntPtr receiver,
        IntPtr selector,
        IntPtr value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    internal static bool TryApply(IntPtr nsWindow)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var clearColor = SendObject(
                objc_getClass("NSColor"),
                sel_registerName("clearColor"));

            SendBool(nsWindow, sel_registerName("setOpaque:"), false);
            SendObject(
                nsWindow,
                sel_registerName("setBackgroundColor:"),
                clearColor);

            var contentView = SendObject(
                nsWindow,
                sel_registerName("contentView"));
            if (contentView != IntPtr.Zero)
            {
                SendBool(
                    contentView,
                    sel_registerName("setWantsLayer:"),
                    true);

                var layer = SendObject(
                    contentView,
                    sel_registerName("layer"));
                if (layer != IntPtr.Zero)
                {
                    SendBool(layer, sel_registerName("setOpaque:"), false);
                    var clearCgColor = SendObject(
                        clearColor,
                        sel_registerName("CGColor"));
                    SendObject(
                        layer,
                        sel_registerName("setBackgroundColor:"),
                        clearCgColor);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
