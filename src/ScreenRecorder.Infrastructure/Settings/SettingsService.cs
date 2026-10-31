// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Infrastructure.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsFilePath = customPath;
        }
        else
        {
            var appData = Storage.UserDataPaths.LocalApplicationData;
            var folder = Path.Combine(appData, "ScreenRecorder");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _settingsFilePath = Path.Combine(folder, "user_settings.json");
        }
    }

    public async Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new UserSettings();
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            if (settings is not null)
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty(nameof(UserSettings.AnnotationShortcuts), out _))
                {
                    // Preserve previously selected recording shortcuts. Pick an unused
                    // annotation key only when the new default collides with them.
                    if (!document.RootElement.TryGetProperty(nameof(UserSettings.StartStopHotkeyModifiers), out _)) settings.StartStopHotkeyModifiers=0;
                    if (!document.RootElement.TryGetProperty(nameof(UserSettings.StartStopHotkeyKey), out _)) settings.StartStopHotkeyKey="F9";
                    if (!document.RootElement.TryGetProperty(nameof(UserSettings.PauseResumeHotkeyKey), out _)) settings.PauseResumeHotkeyKey="F10";
                    var used=new[]{(settings.StartStopHotkeyModifiers,settings.StartStopHotkeyKey),
                        (settings.PauseResumeHotkeyModifiers,settings.PauseResumeHotkeyKey),
                        (settings.BookmarkHotkeyModifiers,settings.BookmarkHotkeyKey)};
                    foreach(var (id,binding) in settings.AnnotationShortcuts)
                    {
                        if(!used.Any(x=>x.Item1==binding.Modifiers && string.Equals(x.Item2,binding.Key,StringComparison.OrdinalIgnoreCase)))continue;
                        var occupied=used.Concat(settings.AnnotationShortcuts.Where(x=>x.Key!=id).Select(x=>(x.Value.Modifiers,x.Value.Key))).ToArray();
                        // Reserve all other defaults too, so migrating one action cannot
                        // displace another. F12 is reserved by Windows even with modifiers.
                        var key=new[]{"F8","F9","F5","F6","F7","F10","F11","F1","F2","F3","F4"}
                            .First(candidate=>!occupied.Any(x=>x.Item1==6 && string.Equals(x.Item2,candidate,StringComparison.OrdinalIgnoreCase)));
                        binding.Modifiers=6;binding.Key=key;
                    }
                }
            }
            return settings ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = _settingsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            if (File.Exists(_settingsFilePath))
            {
                File.Replace(tempPath, _settingsFilePath, null);
            }
            else
            {
                File.Move(tempPath, _settingsFilePath);
            }
        }
        catch
        {
            // 靜態容錯防護，避免因無權限或防毒阻擋造成 UI 崩潰
        }
    }
}
