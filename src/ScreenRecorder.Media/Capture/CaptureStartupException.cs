// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Capture;

public sealed class CaptureStartupException(string message,
    ScreenRecorder.Core.Enums.CaptureBackend compatibilityBackend = ScreenRecorder.Core.Enums.CaptureBackend.Gdi)
    : InvalidOperationException(message)
{
    public ScreenRecorder.Core.Enums.CaptureBackend CompatibilityBackend { get; } = compatibilityBackend;
}
