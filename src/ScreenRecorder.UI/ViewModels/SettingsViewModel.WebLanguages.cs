// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public partial class SettingsViewModel
{
    public record WebLanguageOption(string Code, string DisplayName);
    public ObservableCollection<WebLanguageOption> AvailableWebDefaults { get; } = new();
    private bool _webChineseEnabled = true, _webEnglishEnabled = true, _webJapaneseEnabled = true;
    private WebLanguageOption? _selectedWebDefault;
    public WebLanguageOption? SelectedWebDefault
    {
        get => _selectedWebDefault;
        set => SetProperty(ref _selectedWebDefault, value);
    }
    public bool WebChineseEnabled { get => _webChineseEnabled; set => SetWebLanguage(ref _webChineseEnabled, value, nameof(WebChineseEnabled)); }
    public bool WebEnglishEnabled { get => _webEnglishEnabled; set => SetWebLanguage(ref _webEnglishEnabled, value, nameof(WebEnglishEnabled)); }
    public bool WebJapaneseEnabled { get => _webJapaneseEnabled; set => SetWebLanguage(ref _webJapaneseEnabled, value, nameof(WebJapaneseEnabled)); }
    public bool CanDisableWebChinese => !_webChineseEnabled || _webEnglishEnabled || _webJapaneseEnabled;
    public bool CanDisableWebEnglish => !_webEnglishEnabled || _webChineseEnabled || _webJapaneseEnabled;
    public bool CanDisableWebJapanese => !_webJapaneseEnabled || _webChineseEnabled || _webEnglishEnabled;

    private void SetWebLanguage(ref bool field, bool value, string name)
    {
        if (!value && field && new[] { _webChineseEnabled, _webEnglishEnabled, _webJapaneseEnabled }.Count(x => x) == 1)
        { OnPropertyChanged(name); return; }
        if (SetProperty(ref field, value, name)) RefreshWebDefaults();
    }
    private void RefreshWebDefaults()
    {
        var previous = SelectedWebDefault?.Code ?? "auto";
        AvailableWebDefaults.Clear();
        AvailableWebDefaults.Add(new("auto", Strings["WebLanguagesAuto"]));
        if (_webChineseEnabled) AvailableWebDefaults.Add(new("zh-TW", "繁體中文"));
        if (_webEnglishEnabled) AvailableWebDefaults.Add(new("en", "English"));
        if (_webJapaneseEnabled) AvailableWebDefaults.Add(new("ja", "日本語"));
        SelectedWebDefault = AvailableWebDefaults.FirstOrDefault(x => x.Code == previous) ?? AvailableWebDefaults[0];
        OnPropertyChanged(nameof(CanDisableWebChinese));
        OnPropertyChanged(nameof(CanDisableWebEnglish));
        OnPropertyChanged(nameof(CanDisableWebJapanese));
    }
    private void LoadWebLanguages(WebPlayerLanguageOptions? value)
    {
        var normalized = WebPlayerLanguageOptions.Normalize(value);
        _webChineseEnabled = normalized.EnabledLanguages.Contains("zh-TW");
        _webEnglishEnabled = normalized.EnabledLanguages.Contains("en");
        _webJapaneseEnabled = normalized.EnabledLanguages.Contains("ja");
        RefreshWebDefaults();
        SelectedWebDefault = AvailableWebDefaults.Single(x => x.Code == normalized.DefaultLanguage);
        OnPropertyChanged(nameof(WebChineseEnabled));
        OnPropertyChanged(nameof(WebEnglishEnabled));
        OnPropertyChanged(nameof(WebJapaneseEnabled));
    }
    private WebPlayerLanguageOptions GetWebLanguages() => new()
    {
        EnabledLanguages = AvailableWebDefaults.Where(x => x.Code != "auto").Select(x => x.Code).ToArray(),
        DefaultLanguage = SelectedWebDefault?.Code ?? "auto"
    };
}
