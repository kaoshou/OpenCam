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
