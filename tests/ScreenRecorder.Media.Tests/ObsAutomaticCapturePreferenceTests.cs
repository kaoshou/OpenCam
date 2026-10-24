// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.Windows.Capture;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsAutomaticCapturePreferenceTests
{
    [Fact]
    public async Task AutomaticWithoutAcceptedRuntimeUsesGdiWithoutDesktopDuplicationProbe()
    {
        var config = new RecordingConfiguration { WindowsCaptureMode = (WindowsCaptureMode)2, Fps = 30, AudioSource = AudioSourceType.None };
        var probes = 0;
        var provider = new WindowsCapturePlanProvider(new WindowsFFmpegProvider(), new UnusedCatalog(),
            () => throw new InvalidOperationException("Automatic GDI fallback must not enumerate DDA outputs."),
            _ => { probes++; return Task.FromResult(true); });
        var bounds = new CaptureRegion(-1920, 0, 640, 480);
        var plan = await provider.PrepareAsync(config, bounds, null, default);
        Assert.Equal(0, probes);
        Assert.Equal(CaptureBackend.Gdi, plan.Selection.Backend);
        Assert.Contains("-f gdigrab", plan.VideoInputArguments);
        Assert.DoesNotContain("ddagrab", plan.VideoInputArguments);
        Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(config, bounds, [], [], true).Backend);
        var direct = new WindowsFFmpegProvider().BuildInputArguments(config, bounds.X, bounds.Y, bounds.Width, bounds.Height, false, false);
        Assert.Contains("-f gdigrab", direct);
        Assert.DoesNotContain("ddagrab", direct);
    }

    [Theory]
    [InlineData(AppLanguage.ZhTw, "自動（建議）")]
    [InlineData(AppLanguage.EnUs, "Automatic (recommended)")]
    [InlineData(AppLanguage.JaJp, "自動（推奨）")]
    public async Task WindowsPickerOffersAutomaticAndPreservesManualChoiceAcrossLanguageChanges(AppLanguage language, string automaticLabel)
    {
        var service = new MockSettingsService { CurrentSettings = new() { WindowsCaptureMode = WindowsCaptureMode.CompatibleGdi } };
        var model = new SettingsViewModel(service, true);
        var priorLanguage = model.Strings.CurrentLanguage;
        try
        {
            await model.LoadSettingsAsync();
            model.Strings.CurrentLanguage = language;
            Assert.Equal(3, model.AvailableCaptureModes.Count);
            Assert.Equal(automaticLabel, model.AvailableCaptureModes.Single(option => (int)option.Mode == 2).DisplayName);
            Assert.Equal(WindowsCaptureMode.CompatibleGdi, model.SelectedCaptureMode!.Mode);
            await model.SaveAsync();
            Assert.Equal(WindowsCaptureMode.CompatibleGdi, service.CurrentSettings.WindowsCaptureMode);
            model.SelectedCaptureMode = model.AvailableCaptureModes.Single(option => (int)option.Mode == 2);
            await model.SaveAsync();
            Assert.Equal((WindowsCaptureMode)2, service.CurrentSettings.WindowsCaptureMode);
            model.CanEditCaptureMode = false;
            model.SelectedCaptureMode = model.AvailableCaptureModes.Single(option => option.Mode == WindowsCaptureMode.ModernExperimental);
            await model.SaveAsync();
            Assert.Equal((WindowsCaptureMode)2, service.CurrentSettings.WindowsCaptureMode);
        }
        finally { model.Strings.CurrentLanguage = priorLanguage; }
    }

    private sealed class UnusedCatalog : IDxgiOutputCatalog
    {
        public IReadOnlyList<DxgiOutputInfo> GetOutputs() => throw new InvalidOperationException("Automatic fallback must retain GDI.");
    }
}
