// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.Models;

public sealed record CaptureSelection(CaptureBackend Backend, CaptureFallbackReason FallbackReason,
    string? DeviceName, long? AdapterLuid, int? OutputIndex, CaptureRegion Bounds)
{
    public CaptureRegion? OutputBounds { get; init; }
}
