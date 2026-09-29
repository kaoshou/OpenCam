// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Services;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public class EncoderStatusTests
{
    [Theory]
    [InlineData(AppLanguage.ZhTw, "實際編碼器：尚未決定")]
    [InlineData(AppLanguage.EnUs, "Actual encoder: pending")]
    public void NullSelection_IsPendingInBothLanguages(AppLanguage language, string expected)
    {
        var strings = new LocalizationService { CurrentLanguage = language };
        Assert.Equal(expected, EncoderStatusFormatter.Format(null, strings));
        Assert.Equal(expected, EncoderStatusFormatter.Format(new(HardwareEncoderType.Auto, EncoderFallbackReason.None), strings));
        Assert.Equal(expected, EncoderStatusFormatter.Format(new((HardwareEncoderType)99, EncoderFallbackReason.None), strings));
    }

    [Theory]
    [InlineData(HardwareEncoderType.SoftwareCpu, "libx264 (CPU)")]
    [InlineData(HardwareEncoderType.NvidiaNvenc, "NVENC")]
    [InlineData(HardwareEncoderType.IntelQsv, "QSV")]
    [InlineData(HardwareEncoderType.AmdAmf, "AMF")]
    [InlineData(HardwareEncoderType.AppleVideoToolbox, "VideoToolbox")]
    public void ConfirmedEncoder_HasStableName(HardwareEncoderType type, string name)
    {
        foreach (var language in new[] { AppLanguage.ZhTw, AppLanguage.EnUs })
            Assert.EndsWith(name, EncoderStatusFormatter.Format(new(type, EncoderFallbackReason.None),
                new LocalizationService { CurrentLanguage = language }));
    }

    [Theory]
    [InlineData(EncoderFallbackReason.NoValidatedHardware, "未驗證到可用硬體", "No validated hardware encoder")]
    [InlineData(EncoderFallbackReason.RequestedHardwareUnavailable, "指定硬體暫不可用", "Requested hardware unavailable")]
    [InlineData(EncoderFallbackReason.HardwareStartupFailed, "硬體啟動失敗，使用 CPU", "Hardware startup failed; using CPU")]
    public void FallbackReason_IsLocalized(EncoderFallbackReason reason, string zh, string en)
    {
        var selection = new EncoderSelection(HardwareEncoderType.SoftwareCpu, reason);
        Assert.Contains(zh, EncoderStatusFormatter.Format(selection, new LocalizationService { CurrentLanguage = AppLanguage.ZhTw }));
        Assert.Contains(en, EncoderStatusFormatter.Format(selection, new LocalizationService { CurrentLanguage = AppLanguage.EnUs }));
    }

    [Fact]
    public void RecorderSelection_OverridesUiProbeWithoutChangingPreference()
    {
        var vm = new MainViewModel(forScreenshot: true);
        vm.AvailableEncoders.Add(new MainViewModel.EncoderOption(HardwareEncoderType.IntelQsv, "QSV"));
        vm.ApplyEncoderTelemetry(new() { SessionId = "one", State = RecordingState.Recording,
            EncoderSelection = new(HardwareEncoderType.SoftwareCpu, EncoderFallbackReason.HardwareStartupFailed) });
        Assert.Contains("libx264", vm.ActualEncoderText);
        Assert.Equal(HardwareEncoderType.Auto, vm.SelectedEncoder!.Type);
        vm.ApplyEncoderTelemetry(new() { SessionId = "one", State = RecordingState.Paused,
            EncoderSelection = new(HardwareEncoderType.SoftwareCpu, EncoderFallbackReason.HardwareStartupFailed) });
        Assert.Contains("libx264", vm.ActualEncoderText);
        var previous = vm.Strings.CurrentLanguage;
        try
        {
            vm.Strings.CurrentLanguage = AppLanguage.EnUs;
            Assert.StartsWith("Actual encoder:", vm.ActualEncoderText);
            vm.Strings.CurrentLanguage = AppLanguage.ZhTw;
            Assert.StartsWith("實際編碼器：", vm.ActualEncoderText);
        }
        finally { vm.Strings.CurrentLanguage = previous; }
    }

    [Fact]
    public void NewSessionAndTerminalState_ClearOldEncoder()
    {
        var vm = new MainViewModel(forScreenshot: true);
        vm.ApplyEncoderTelemetry(new() { SessionId = "old", State = RecordingState.Recording,
            EncoderSelection = new(HardwareEncoderType.IntelQsv, EncoderFallbackReason.None) });
        Assert.Contains("QSV", vm.ActualEncoderText);
        vm.IsPreparing = true;
        Assert.DoesNotContain("QSV", vm.ActualEncoderText);
        // In-flight old telemetry must not repopulate a new pending start.
        vm.ApplyEncoderTelemetry(new() { SessionId = "old", State = RecordingState.Recording,
            EncoderSelection = new(HardwareEncoderType.IntelQsv, EncoderFallbackReason.None) });
        Assert.DoesNotContain("QSV", vm.ActualEncoderText);
        vm.IsPreparing = false;
        vm.ApplyEncoderTelemetry(new() { SessionId = "new", State = RecordingState.Recording });
        Assert.DoesNotContain("QSV", vm.ActualEncoderText);
        vm.ApplyEncoderTelemetry(new() { State = RecordingState.Idle });
        Assert.DoesNotContain("libx264", vm.ActualEncoderText);
    }

    [Fact]
    public void LegacySessionAndTelemetry_RemainReadable()
    {
        Assert.Null(JsonSerializer.Deserialize<RecordingSession>("{\"SessionId\":\"legacy\"}")!.EncoderSelection);
        Assert.Null(JsonSerializer.Deserialize<RecorderTelemetry>("{\"SessionId\":\"legacy\"}")!.EncoderSelection);
        var telemetry = new RecorderTelemetry { EncoderSelection = new(HardwareEncoderType.IntelQsv, EncoderFallbackReason.None) };
        Assert.Equal(telemetry.EncoderSelection, JsonSerializer.Deserialize<RecorderTelemetry>(JsonSerializer.Serialize(telemetry))!.EncoderSelection);
    }
}
