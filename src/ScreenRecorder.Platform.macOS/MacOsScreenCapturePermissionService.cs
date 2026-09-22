// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace ScreenRecorder.Platform.macOS;

internal interface IMacOsScreenCapturePermissionApi
{
    bool Preflight();
    bool Request();
}

internal sealed class CoreGraphicsScreenCapturePermissionApi
    : IMacOsScreenCapturePermissionApi
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();

    public bool Preflight() => CGPreflightScreenCaptureAccess();

    public bool Request() => CGRequestScreenCaptureAccess();
}

public interface IMacOsScreenCapturePermissionService
{
    bool HasPermission();
    bool RequestPermission();
}

public sealed class MacOsScreenCapturePermissionService
    : IMacOsScreenCapturePermissionService
{
    private readonly IMacOsScreenCapturePermissionApi _api;

    public MacOsScreenCapturePermissionService()
        : this(new CoreGraphicsScreenCapturePermissionApi())
    {
    }

    internal MacOsScreenCapturePermissionService(
        IMacOsScreenCapturePermissionApi api)
    {
        _api = api;
    }

    public bool HasPermission() => _api.Preflight();

    public bool RequestPermission() => _api.Request();
}
