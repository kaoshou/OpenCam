// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Services;
using ScreenRecorder.UI.ViewModels;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class CaptureStatusTests
{
    [Theory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public void ActualBackendAndFallbackAreNotConfusedWithPreference(AppLanguage language)
    {
        var strings = new LocalizationService { CurrentLanguage = language };
        var selection = new CaptureSelection(CaptureBackend.Gdi, CaptureFallbackReason.StartupFailed, null, null, null, new(0, 0, 640, 480));
        var text = CaptureStatusFormatter.Format(selection, strings);
        Assert.Contains("GDI", text);
        Assert.Contains(strings["CaptureFallbackStartup"], text);
        Assert.DoesNotContain("Desktop Duplication", text);
        Assert.Contains("Desktop Duplication", CaptureStatusFormatter.Format(selection with { Backend = CaptureBackend.DesktopDuplication, FallbackReason = CaptureFallbackReason.None }, strings));
    }

    [Fact]
    public void NewSessionClearsOldBackend()
    {
        var vm = new MainViewModel(forScreenshot: true);
        vm.ApplyEncoderTelemetry(new() { SessionId = "old", State = RecordingState.Recording,
            CaptureSelection = new(CaptureBackend.DesktopDuplication, CaptureFallbackReason.None, "screen", 1, 0, new(0, 0, 640, 480)) });
        Assert.Contains("Desktop Duplication", vm.ActualCaptureText);
        vm.IsPreparing = true;
        Assert.DoesNotContain("Desktop Duplication", vm.ActualCaptureText);
    }

    [Theory]
    [InlineData(true, true, WindowsCaptureMode.ModernExperimental)]
    [InlineData(true, false, WindowsCaptureMode.CompatibleGdi)]
    [InlineData(false, true, WindowsCaptureMode.CompatibleGdi)]
    public async Task OnlyEditableWindowsSettingsCanEnableNewCapture(bool windows, bool editable, WindowsCaptureMode expected)
    {
        var service = new MockSettingsService();
        var vm = new SettingsViewModel(service, windows);
        await vm.LoadSettingsAsync();
        vm.CanEditCaptureMode = editable;
        vm.SelectedCaptureMode = vm.AvailableCaptureModes.Single(o => o.Mode == WindowsCaptureMode.ModernExperimental);
        await vm.SaveAsync();
        Assert.Equal(expected, service.CurrentSettings.WindowsCaptureMode);
        Assert.Equal(windows, vm.SupportsWindowsCapture);
    }
}
