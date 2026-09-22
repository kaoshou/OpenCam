// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; set; }
    string this[string key] { get; }
    string GetString(string key);
    string GetFormatted(string key, params object[] args);
    event EventHandler<AppLanguage>? LanguageChanged;
}
