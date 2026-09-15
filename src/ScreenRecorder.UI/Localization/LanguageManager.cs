using System.ComponentModel;
using ScreenRecorder.Core.Localization;

namespace ScreenRecorder.UI.Localization;

public class LanguageManager : INotifyPropertyChanged
{
    private static readonly Lazy<LanguageManager> _instance = new(() => new LanguageManager());
    public static LanguageManager Instance => _instance.Value;

    private readonly ILocalizationService _localizationService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LanguageManager()
    {
        _localizationService = LocalizationService.Instance;
        _localizationService.LanguageChanged += (s, lang) =>
        {
            // 通知所有綁定更新 (完整相容 Avalonia CompiledBinding 與索引子監聽)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZhTw)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnUs)));
        };
    }

    public string this[string key] => _localizationService.GetString(key);

    public string GetString(string key) => _localizationService.GetString(key);

    public string GetFormatted(string key, params object[] args) => _localizationService.GetFormatted(key, args);

    public AppLanguage CurrentLanguage
    {
        get => _localizationService.CurrentLanguage;
        set => _localizationService.CurrentLanguage = value;
    }

    public bool IsZhTw => CurrentLanguage == AppLanguage.ZhTw;
    public bool IsEnUs => CurrentLanguage == AppLanguage.EnUs;

    public void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == AppLanguage.ZhTw ? AppLanguage.EnUs : AppLanguage.ZhTw;
    }
}
