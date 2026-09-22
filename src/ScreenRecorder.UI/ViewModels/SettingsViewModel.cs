// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public LanguageManager Strings => LanguageManager.Instance;

    private readonly ISettingsService _settingsService;
    private UserSettings _currentSettings = new();

    public event Action? RequestClose;
    public event Action<UserSettings>? SettingsSaved;

    // 項目定義
    public record ModifierOption(uint Value, string DisplayName);
    public record KeyOption(string KeyName, int VirtualKey);
    public record LanguageOption(AppLanguage Language, string DisplayName);
    public record QualityOption(string PresetKey, string DisplayName);
    public record DiskThresholdOption(double Value, string DisplayName);

    public ObservableCollection<ModifierOption> AvailableModifiers { get; } = new();
    public ObservableCollection<KeyOption> AvailableKeys { get; } = new();
    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = new();
    public ObservableCollection<QualityOption> AvailableQualities { get; } = new();
    public ObservableCollection<DiskThresholdOption> AvailableWarningThresholds { get; } = new();
    public ObservableCollection<DiskThresholdOption> AvailableCriticalThresholds { get; } = new();

    // 綁定屬性
    [ObservableProperty]
    private ModifierOption? _selectedStartStopModifier;

    [ObservableProperty]
    private KeyOption? _selectedStartStopKey;

    [ObservableProperty]
    private ModifierOption? _selectedPauseResumeModifier;

    [ObservableProperty]
    private KeyOption? _selectedPauseResumeKey;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private QualityOption? _selectedQuality;

    [ObservableProperty]
    private DiskThresholdOption? _selectedWarningThreshold;

    [ObservableProperty]
    private DiskThresholdOption? _selectedCriticalThreshold;

    [ObservableProperty]
    private bool _openFolderOnFinished;

    [ObservableProperty]
    private bool _deleteWorkingFileAfterRemux;

    [ObservableProperty]
    private bool _minimizeOnRecord;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0: 偏好設定, 1: 關於本程式

    public bool IsPreferencesTab => SelectedTabIndex == 0;
    public bool IsAboutTab => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPreferencesTab));
        OnPropertyChanged(nameof(IsAboutTab));
    }

    [RelayCommand]
    public void SwitchToTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }

    [RelayCommand]
    public void ShowPreferencesTab()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    public void ShowAboutTab()
    {
        SelectedTabIndex = 1;
    }

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/kaoshou/OpenCam")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeOptions();
        Strings.PropertyChanged += (s, e) => RefreshOptionLabels();
    }

    private void InitializeOptions()
    {
        // 1. 修飾鍵清單 (0=None, 1=Alt, 2=Ctrl, 4=Shift, 3=Ctrl+Alt, 6=Ctrl+Shift, 5=Alt+Shift)
        AvailableModifiers.Clear();
        AvailableModifiers.Add(new ModifierOption(0, "None"));
        AvailableModifiers.Add(new ModifierOption(2, "Ctrl"));
        AvailableModifiers.Add(new ModifierOption(1, "Alt"));
        AvailableModifiers.Add(new ModifierOption(4, "Shift"));
        AvailableModifiers.Add(new ModifierOption(6, "Ctrl + Shift"));
        AvailableModifiers.Add(new ModifierOption(3, "Ctrl + Alt"));
        AvailableModifiers.Add(new ModifierOption(5, "Alt + Shift"));

        // 2. 主按鍵清單 (F1~F12, R, S, P, Space, Insert, Home)
        AvailableKeys.Clear();
        for (int i = 1; i <= 12; i++)
        {
            AvailableKeys.Add(new KeyOption($"F{i}", 0x70 + (i - 1)));
        }
        AvailableKeys.Add(new KeyOption("R", 0x52));
        AvailableKeys.Add(new KeyOption("S", 0x53));
        AvailableKeys.Add(new KeyOption("P", 0x50));
        AvailableKeys.Add(new KeyOption("Space", 0x20));
        AvailableKeys.Add(new KeyOption("Insert", 0x2D));
        AvailableKeys.Add(new KeyOption("Home", 0x24));

        // 3. 語言清單
        AvailableLanguages.Clear();
        AvailableLanguages.Add(new LanguageOption(AppLanguage.ZhTw, Strings["LanguageOptionZhTw"]));
        AvailableLanguages.Add(new LanguageOption(AppLanguage.EnUs, Strings["LanguageOptionEnUs"]));

        // 4. 畫質品質清單
        AvailableQualities.Clear();
        AvailableQualities.Add(new QualityOption("Ultra", Strings["QualityUltra"]));
        AvailableQualities.Add(new QualityOption("Standard", Strings["QualityStandard"]));
        AvailableQualities.Add(new QualityOption("Compact", Strings["QualityCompact"]));

        // 5. 磁碟警示門檻 (GB) & 危急門檻 (MB)
        PopulateDiskThresholdOptions();
    }

    private void PopulateDiskThresholdOptions()
    {
        var warnVal = SelectedWarningThreshold?.Value ?? 2.0;
        AvailableWarningThresholds.Clear();
        AvailableWarningThresholds.Add(new DiskThresholdOption(1.0, "1.0 GB"));
        AvailableWarningThresholds.Add(new DiskThresholdOption(2.0, $"2.0 GB ({Strings["RecommendedTag"]})"));
        AvailableWarningThresholds.Add(new DiskThresholdOption(5.0, "5.0 GB"));
        AvailableWarningThresholds.Add(new DiskThresholdOption(10.0, "10.0 GB"));
        SelectedWarningThreshold = AvailableWarningThresholds.FirstOrDefault(w => Math.Abs(w.Value - warnVal) < 0.1) ?? AvailableWarningThresholds[1];

        var critVal = SelectedCriticalThreshold?.Value ?? 500.0;
        AvailableCriticalThresholds.Clear();
        AvailableCriticalThresholds.Add(new DiskThresholdOption(200.0, "200 MB"));
        AvailableCriticalThresholds.Add(new DiskThresholdOption(500.0, $"500 MB ({Strings["RecommendedTag"]})"));
        AvailableCriticalThresholds.Add(new DiskThresholdOption(1000.0, "1000 MB (1.0 GB)"));
        SelectedCriticalThreshold = AvailableCriticalThresholds.FirstOrDefault(c => Math.Abs(c.Value - critVal) < 1.0) ?? AvailableCriticalThresholds[1];
    }

    private void RefreshOptionLabels()
    {
        var lang = SelectedLanguage?.Language ?? AppLanguage.ZhTw;
        AvailableLanguages.Clear();
        AvailableLanguages.Add(new LanguageOption(AppLanguage.ZhTw, Strings["LanguageOptionZhTw"]));
        AvailableLanguages.Add(new LanguageOption(AppLanguage.EnUs, Strings["LanguageOptionEnUs"]));
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Language == lang) ?? AvailableLanguages[0];

        var quality = SelectedQuality?.PresetKey ?? "Standard";
        AvailableQualities.Clear();
        AvailableQualities.Add(new QualityOption("Ultra", Strings["QualityUltra"]));
        AvailableQualities.Add(new QualityOption("Standard", Strings["QualityStandard"]));
        AvailableQualities.Add(new QualityOption("Compact", Strings["QualityCompact"]));
        SelectedQuality = AvailableQualities.FirstOrDefault(q => q.PresetKey == quality) ?? AvailableQualities[1];

        PopulateDiskThresholdOptions();
    }

    public async Task LoadSettingsAsync()
    {
        _currentSettings = await _settingsService.LoadSettingsAsync();

        SelectedStartStopModifier = AvailableModifiers.FirstOrDefault(m => m.Value == _currentSettings.StartStopHotkeyModifiers)
                                    ?? AvailableModifiers[0];
        SelectedStartStopKey = AvailableKeys.FirstOrDefault(k => k.KeyName.Equals(_currentSettings.StartStopHotkeyKey, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableKeys.FirstOrDefault(k => k.KeyName == "F9");

        SelectedPauseResumeModifier = AvailableModifiers.FirstOrDefault(m => m.Value == _currentSettings.PauseResumeHotkeyModifiers)
                                      ?? AvailableModifiers[0];
        SelectedPauseResumeKey = AvailableKeys.FirstOrDefault(k => k.KeyName.Equals(_currentSettings.PauseResumeHotkeyKey, StringComparison.OrdinalIgnoreCase))
                                 ?? AvailableKeys.FirstOrDefault(k => k.KeyName == "F10");

        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Language == _currentSettings.Language)
                           ?? AvailableLanguages[0];

        SelectedQuality = AvailableQualities.FirstOrDefault(q => q.PresetKey.Equals(_currentSettings.VideoQualityPreset, StringComparison.OrdinalIgnoreCase))
                          ?? AvailableQualities[1];

        SelectedWarningThreshold = AvailableWarningThresholds.FirstOrDefault(w => Math.Abs(w.Value - _currentSettings.DiskWarningThresholdGb) < 0.1)
                                   ?? AvailableWarningThresholds[1];

        SelectedCriticalThreshold = AvailableCriticalThresholds.FirstOrDefault(c => Math.Abs(c.Value - _currentSettings.DiskCriticalThresholdMb) < 1.0)
                                    ?? AvailableCriticalThresholds[1];

        OpenFolderOnFinished = _currentSettings.OpenFolderOnFinished;
        DeleteWorkingFileAfterRemux = _currentSettings.DeleteWorkingFileAfterSuccessfulRemux;
        MinimizeOnRecord = _currentSettings.MinimizeOnRecord;

        ErrorMessage = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        ErrorMessage = null;

        // 1. 快捷鍵防衝突校驗
        if (SelectedStartStopModifier?.Value == SelectedPauseResumeModifier?.Value &&
            SelectedStartStopKey?.KeyName == SelectedPauseResumeKey?.KeyName)
        {
            ErrorMessage = Strings["HotkeyConflictError"];
            return;
        }

        // 2. 構建更新後之設定物件
        _currentSettings.StartStopHotkeyModifiers = SelectedStartStopModifier?.Value ?? 0;
        _currentSettings.StartStopHotkeyKey = SelectedStartStopKey?.KeyName ?? "F9";
        _currentSettings.PauseResumeHotkeyModifiers = SelectedPauseResumeModifier?.Value ?? 0;
        _currentSettings.PauseResumeHotkeyKey = SelectedPauseResumeKey?.KeyName ?? "F10";

        _currentSettings.Language = SelectedLanguage?.Language ?? AppLanguage.ZhTw;
        _currentSettings.VideoQualityPreset = SelectedQuality?.PresetKey ?? "Standard";
        _currentSettings.DiskWarningThresholdGb = SelectedWarningThreshold?.Value ?? 2.0;
        _currentSettings.DiskCriticalThresholdMb = SelectedCriticalThreshold?.Value ?? 500.0;
        _currentSettings.OpenFolderOnFinished = OpenFolderOnFinished;
        _currentSettings.DeleteWorkingFileAfterSuccessfulRemux = DeleteWorkingFileAfterRemux;
        _currentSettings.MinimizeOnRecord = MinimizeOnRecord;

        // 3. 即時套用語言
        if (Strings.CurrentLanguage != _currentSettings.Language)
        {
            Strings.CurrentLanguage = _currentSettings.Language;
        }

        // 4. 儲存設定至 JSON 檔案
        await _settingsService.SaveSettingsAsync(_currentSettings);

        SuccessMessage = Strings["SettingsSavedSuccess"];

        // 5. 通知主視窗更新
        SettingsSaved?.Invoke(_currentSettings);

        await Task.Delay(400);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        SelectedStartStopModifier = AvailableModifiers[0]; // None
        SelectedStartStopKey = AvailableKeys.FirstOrDefault(k => k.KeyName == "F9");

        SelectedPauseResumeModifier = AvailableModifiers[0]; // None
        SelectedPauseResumeKey = AvailableKeys.FirstOrDefault(k => k.KeyName == "F10");

        SelectedLanguage = AvailableLanguages[0]; // ZhTw
        SelectedQuality = AvailableQualities[1];  // Standard (CRF 23)
        SelectedWarningThreshold = AvailableWarningThresholds[1]; // 2.0 GB
        SelectedCriticalThreshold = AvailableCriticalThresholds[1]; // 500 MB

        OpenFolderOnFinished = false;
        DeleteWorkingFileAfterRemux = false;
        MinimizeOnRecord = false;

        ErrorMessage = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke();
    }
}
