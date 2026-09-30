// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Capture;

public sealed class CaptureStartupException(string message) : InvalidOperationException(message);
