// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Settings;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFile;

    public SettingsServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"test_settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    [Fact]
    public async Task LoadSettings_WhenFileDoesNotExist_ShouldReturnDefaultSettings()
    {
        var service = new SettingsService(_tempFile);
        var settings = await service.LoadSettingsAsync();

        Assert.NotNull(settings);
        Assert.Equal(30, settings.Fps);
        Assert.Equal(AppLanguage.ZhTw, settings.Language);
        Assert.Equal(HardwareEncoderType.Auto, settings.EncoderType);
        Assert.Equal(CursorEffectMode.Default, settings.CursorEffect);
        Assert.True(settings.RecordSystemAudio);
        Assert.False(settings.RecordMicrophone);
    }

    [Fact]
    public async Task SaveAndLoadSettings_ShouldPreserveValues()
    {
        var service = new SettingsService(_tempFile);
        var original = new UserSettings
        {
            Fps = 60,
            Language = AppLanguage.EnUs,
            EncoderType = HardwareEncoderType.NvidiaNvenc,
            CursorEffect = CursorEffectMode.HighlightHalo,
            RecordSystemAudio = false,
            RecordMicrophone = true,
            SelectedMicrophoneId = "usb_mic_1",
            CaptureRangeMode = "CustomRegion",
            RegionX = 200,
            RegionY = 150,
            RegionWidth = 1920,
            RegionHeight = 1080,
            CustomOutputDirectory = @"D:\MyRecordings",
            MinimizeOnRecord = true
        };

        await service.SaveSettingsAsync(original);
        var loaded = await service.LoadSettingsAsync();

        Assert.NotNull(loaded);
        Assert.Equal(60, loaded.Fps);
        Assert.Equal(AppLanguage.EnUs, loaded.Language);
        Assert.Equal(HardwareEncoderType.NvidiaNvenc, loaded.EncoderType);
        Assert.Equal(CursorEffectMode.HighlightHalo, loaded.CursorEffect);
        Assert.False(loaded.RecordSystemAudio);
        Assert.True(loaded.RecordMicrophone);
        Assert.Equal("usb_mic_1", loaded.SelectedMicrophoneId);
        Assert.Equal("CustomRegion", loaded.CaptureRangeMode);
        Assert.Equal(200, loaded.RegionX);
        Assert.Equal(150, loaded.RegionY);
        Assert.Equal(1920, loaded.RegionWidth);
        Assert.Equal(1080, loaded.RegionHeight);
        Assert.Equal(@"D:\MyRecordings", loaded.CustomOutputDirectory);
        Assert.True(loaded.MinimizeOnRecord);
    }

    [Fact]
    public async Task LoadSettings_WhenFileCorrupted_ShouldFallbackToDefaults()
    {
        await File.WriteAllTextAsync(_tempFile, "{ corrupted json !!!");
        var service = new SettingsService(_tempFile);
        var loaded = await service.LoadSettingsAsync();

        Assert.NotNull(loaded);
        Assert.Equal(30, loaded.Fps);
        Assert.Equal(AppLanguage.ZhTw, loaded.Language);
    }
}
