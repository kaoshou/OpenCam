// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core;
using ScreenRecorder.Infrastructure.Settings;

if (args is ["--settings-probe"])
{
    var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(xdg) || !Path.IsPathFullyQualified(xdg) || Directory.Exists(xdg)) return 2;
    var settings = new SettingsService();
    await settings.SaveSettingsAsync(new ScreenRecorder.Core.Models.UserSettings { Fps = 20 });
    var expected = Path.Combine(xdg, "ScreenRecorder", "user_settings.json");
    Console.WriteLine(expected);
    return File.Exists(expected) && (await settings.LoadSettingsAsync()).Fps == 20 ? 0 : 1;
}

Console.WriteLine(ProductInfo.PackageDisplayVersion);
return ProductInfo.PackageDisplayVersion == $"v{ProductInfo.Version} (Linux x64 Beta Build)" ? 0 : 1;
