using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class MockSettingsService : ISettingsService
{
    public UserSettings CurrentSettings { get; set; } = new();
    public int SaveCount { get; private set; }

    public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentSettings);
    }

    public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        CurrentSettings = settings;
        SaveCount++;
        return Task.CompletedTask;
    }
}

public class SettingsViewModelTests
{
    [Fact]
    public async Task SettingsViewModel_LoadSettings_ShouldBindPropertiesCorrectly()
    {
        var mockService = new MockSettingsService
        {
            CurrentSettings = new UserSettings
            {
                StartStopHotkeyModifiers = 2, // Ctrl
                StartStopHotkeyKey = "F8",
                PauseResumeHotkeyModifiers = 3, // Ctrl+Alt
                PauseResumeHotkeyKey = "F11",
                Language = AppLanguage.EnUs,
                VideoQualityPreset = "Ultra",
                DiskWarningThresholdGb = 5.0,
                DiskCriticalThresholdMb = 1000.0,
                OpenFolderOnFinished = true,
                DeleteWorkingFileAfterSuccessfulRemux = true,
                MinimizeOnRecord = true
            }
        };

        var vm = new SettingsViewModel(mockService);
        await vm.LoadSettingsAsync();

        Assert.NotNull(vm.SelectedStartStopModifier);
        Assert.Equal((uint)2, vm.SelectedStartStopModifier.Value);
        Assert.NotNull(vm.SelectedStartStopKey);
        Assert.Equal("F8", vm.SelectedStartStopKey.KeyName);

        Assert.NotNull(vm.SelectedPauseResumeModifier);
        Assert.Equal((uint)3, vm.SelectedPauseResumeModifier.Value);
        Assert.NotNull(vm.SelectedPauseResumeKey);
        Assert.Equal("F11", vm.SelectedPauseResumeKey.KeyName);

        Assert.NotNull(vm.SelectedLanguage);
        Assert.Equal(AppLanguage.EnUs, vm.SelectedLanguage.Language);

        Assert.NotNull(vm.SelectedQuality);
        Assert.Equal("Ultra", vm.SelectedQuality.PresetKey);

        Assert.True(vm.OpenFolderOnFinished);
        Assert.True(vm.DeleteWorkingFileAfterRemux);
        Assert.True(vm.MinimizeOnRecord);
    }

    [Fact]
    public async Task SettingsViewModel_Save_HotkeyConflict_ShouldSetErrorMessageAndNotSave()
    {
        var mockService = new MockSettingsService();
        var vm = new SettingsViewModel(mockService);
        await vm.LoadSettingsAsync();

        // 人為設定兩組快捷鍵一模一樣 (例如皆為 None + F9)
        vm.SelectedStartStopModifier = vm.AvailableModifiers[0]; // None
        vm.SelectedStartStopKey = vm.AvailableKeys.First(k => k.KeyName == "F9");

        vm.SelectedPauseResumeModifier = vm.AvailableModifiers[0]; // None
        vm.SelectedPauseResumeKey = vm.AvailableKeys.First(k => k.KeyName == "F9");

        await vm.SaveAsync();

        // 應觸發防呆衝突警告
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Equal(0, mockService.SaveCount);
    }

    [Fact]
    public async Task SettingsViewModel_Save_ValidSettings_ShouldInvokeSavedEventAndPersist()
    {
        var mockService = new MockSettingsService();
        var vm = new SettingsViewModel(mockService);
        await vm.LoadSettingsAsync();

        // 設定無衝突快捷鍵
        vm.SelectedStartStopModifier = vm.AvailableModifiers[0]; // None
        vm.SelectedStartStopKey = vm.AvailableKeys.First(k => k.KeyName == "F7");

        vm.SelectedPauseResumeModifier = vm.AvailableModifiers[1]; // Alt
        vm.SelectedPauseResumeKey = vm.AvailableKeys.First(k => k.KeyName == "F8");

        vm.OpenFolderOnFinished = true;

        UserSettings? savedEventPayload = null;
        vm.SettingsSaved += (settings) =>
        {
            savedEventPayload = settings;
        };

        await vm.SaveAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(1, mockService.SaveCount);
        Assert.NotNull(savedEventPayload);
        Assert.Equal("F7", savedEventPayload.StartStopHotkeyKey);
        Assert.Equal("F8", savedEventPayload.PauseResumeHotkeyKey);
        Assert.True(savedEventPayload.OpenFolderOnFinished);
    }

    [Fact]
    public void SettingsViewModel_ResetToDefaults_ShouldRestoreStandardValues()
    {
        var mockService = new MockSettingsService();
        var vm = new SettingsViewModel(mockService);

        // 先改變一些值
        vm.OpenFolderOnFinished = true;
        vm.MinimizeOnRecord = true;

        // 執行重設
        vm.ResetToDefaults();

        Assert.Equal("F9", vm.SelectedStartStopKey?.KeyName);
        Assert.Equal("F10", vm.SelectedPauseResumeKey?.KeyName);
        Assert.Equal(AppLanguage.ZhTw, vm.SelectedLanguage?.Language);
        Assert.Equal("Standard", vm.SelectedQuality?.PresetKey);
        Assert.False(vm.OpenFolderOnFinished);
        Assert.False(vm.DeleteWorkingFileAfterRemux);
        Assert.False(vm.MinimizeOnRecord);
    }

    [Fact]
    public void SettingsViewModel_TabNavigation_ShouldSwitchCorrectly()
    {
        var mockService = new MockSettingsService();
        var vm = new SettingsViewModel(mockService);

        // 預設為 0 (偏好設定)
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsPreferencesTab);
        Assert.False(vm.IsAboutTab);

        // 切換到 1 (關於本程式)
        vm.SwitchToTab(1);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.False(vm.IsPreferencesTab);
        Assert.True(vm.IsAboutTab);

        // 切換回 0
        vm.SwitchToTab(0);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsPreferencesTab);
        Assert.False(vm.IsAboutTab);
    }
}
