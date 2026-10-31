// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Linux;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxEncoderSelectionTests
{
    [Fact]
    public async Task LinuxOffersOnlyTheEncoderEnabledByItsRecordingRuntime()
    {
        var service = new LinuxEncoderSelectionService();
        var capability = Assert.Single(await service.DetectAvailableEncodersAsync());
        Assert.True(capability.IsAvailable);
        Assert.Equal(HardwareEncoderType.SoftwareCpu, capability.Type);
    }

    [Theory]
    [InlineData(HardwareEncoderType.Auto, EncoderFallbackReason.NoValidatedHardware)]
    [InlineData(HardwareEncoderType.SoftwareCpu, EncoderFallbackReason.None)]
    [InlineData(HardwareEncoderType.NvidiaNvenc, EncoderFallbackReason.RequestedHardwareUnavailable)]
    [InlineData(HardwareEncoderType.IntelQsv, EncoderFallbackReason.RequestedHardwareUnavailable)]
    [InlineData(HardwareEncoderType.AmdAmf, EncoderFallbackReason.RequestedHardwareUnavailable)]
    [InlineData(HardwareEncoderType.AppleVideoToolbox, EncoderFallbackReason.RequestedHardwareUnavailable)]
    public async Task SavedHardwarePreferencesResolveToTheEnabledLinuxEncoder(HardwareEncoderType saved, EncoderFallbackReason reason)
    {
        var service = new LinuxEncoderSelectionService();
        Assert.Equal(new EncoderSelection(HardwareEncoderType.SoftwareCpu, reason), await service.SelectAsync(saved));
        Assert.Equal(HardwareEncoderType.SoftwareCpu, await service.ResolveOptimalEncoderAsync(saved));
    }

    [Fact]
    public async Task CancelledSelectionDoesNotPublishAResult()
    {
        var service = new LinuxEncoderSelectionService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DetectAvailableEncodersAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SelectAsync(HardwareEncoderType.Auto, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveOptimalEncoderAsync(HardwareEncoderType.Auto, cancellation.Token));
    }
}
