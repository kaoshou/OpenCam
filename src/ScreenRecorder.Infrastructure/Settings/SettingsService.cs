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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
                    var pen=settings.AnnotationShortcuts["togglePen"];
                    var used=new[]{(settings.StartStopHotkeyModifiers,settings.StartStopHotkeyKey),
                        (settings.PauseResumeHotkeyModifiers,settings.PauseResumeHotkeyKey),
                        (settings.BookmarkHotkeyModifiers,settings.BookmarkHotkeyKey)};
                    if(used.Any(x=>x.Item1==pen.Modifiers && string.Equals(x.Item2,pen.Key,StringComparison.OrdinalIgnoreCase)))
                    {
                        var occupied=used.Concat(settings.AnnotationShortcuts.Where(x=>x.Key!="togglePen").Select(x=>(x.Value.Modifiers,x.Value.Key))).ToArray();
                        foreach(var key in new[]{"F8","F9","F5","F6","F7","F12"})
                            if(!occupied.Any(x=>x.Item1==6 && string.Equals(x.Item2,key,StringComparison.OrdinalIgnoreCase)))
                            { pen.Modifiers=6;pen.Key=key;break; }
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
