// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Linux;

/// <summary>The Linux Beta runtime enables CPU encoding only; never probe an unrelated FFmpeg hardware encoder.</summary>
public sealed class LinuxEncoderSelectionService : IEncoderSelectionService
{
    public Task<IReadOnlyList<EncoderCapability>> DetectAvailableEncodersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<EncoderCapability>>([
            new(HardwareEncoderType.SoftwareCpu, "libx264", "CPU", true)
        ]);
    }

    public Task<EncoderSelection> SelectAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reason = preferred switch {
            HardwareEncoderType.SoftwareCpu => EncoderFallbackReason.None,
            HardwareEncoderType.Auto => EncoderFallbackReason.NoValidatedHardware,
            _ => EncoderFallbackReason.RequestedHardwareUnavailable
        };
        return Task.FromResult(new EncoderSelection(HardwareEncoderType.SoftwareCpu, reason));
    }

    public async Task<HardwareEncoderType> ResolveOptimalEncoderAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default) =>
        (await SelectAsync(preferred, cancellationToken)).Encoder;

    // CPU is the sole supported choice; no hardware probe cache is retained.
    public void ReportStartupFailure(HardwareEncoderType encoder) { }
}
