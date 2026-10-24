// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public sealed record WebPlayerLanguageOptions
{
    public string[] EnabledLanguages { get; init; } = ["zh-TW", "en", "ja"];
    public string DefaultLanguage { get; init; } = "auto";

    public static WebPlayerLanguageOptions Normalize(WebPlayerLanguageOptions? value)
    {
        string[] supported = ["zh-TW", "en", "ja"];
        var enabled = supported.Where(code => value?.EnabledLanguages?.Contains(code, StringComparer.Ordinal) == true).ToArray();
        if (enabled.Length == 0) enabled = supported;
        return new()
        {
            EnabledLanguages = enabled,
            DefaultLanguage = enabled.Contains(value?.DefaultLanguage, StringComparer.Ordinal) ? value!.DefaultLanguage : "auto"
        };
    }
}
