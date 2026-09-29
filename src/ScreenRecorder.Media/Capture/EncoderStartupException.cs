// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Capture;

/// <summary>One FFmpeg attempt failed before confirming its first frame.</summary>
public sealed class EncoderStartupException(string message) : InvalidOperationException(message);
