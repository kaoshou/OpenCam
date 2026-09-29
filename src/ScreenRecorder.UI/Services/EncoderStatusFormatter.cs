// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.UI.Services;

internal static class EncoderStatusFormatter
{
    internal static string Format(EncoderSelection? selection, ILocalizationService strings)
    {
        var name = selection?.Encoder switch
        {
            HardwareEncoderType.SoftwareCpu => "libx264 (CPU)",
            HardwareEncoderType.NvidiaNvenc => "NVENC",
            HardwareEncoderType.IntelQsv => "QSV",
            HardwareEncoderType.AmdAmf => "AMF",
            HardwareEncoderType.AppleVideoToolbox => "VideoToolbox",
            _ => null
        };
        if (name == null) return strings["ActualEncoderPending"];
        var reason = selection!.FallbackReason switch
        {
            EncoderFallbackReason.NoValidatedHardware => "EncoderFallbackNoHardware",
            EncoderFallbackReason.RequestedHardwareUnavailable => "EncoderFallbackRequested",
            EncoderFallbackReason.HardwareStartupFailed => "EncoderFallbackStartup",
            _ => null
        };
        return strings.GetFormatted("ActualEncoderFormat", name) +
            (reason == null ? string.Empty : " · " + strings[reason]);
    }
}
