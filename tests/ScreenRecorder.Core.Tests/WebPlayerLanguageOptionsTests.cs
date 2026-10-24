using ScreenRecorder.Core.Models;
using System.Text.Json;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class WebPlayerLanguageOptionsTests
{
    [Fact]
    public void MissingOrEmptyPolicyRestoresUsableDefaults()
    {
        foreach (var input in new WebPlayerLanguageOptions?[] { null, new() { EnabledLanguages = [] },
            new() { EnabledLanguages = null! }, new() { EnabledLanguages = ["invalid"] } })
        {
            var result = WebPlayerLanguageOptions.Normalize(input);
            Assert.Equal(new[] { "zh-TW", "en", "ja" }, result.EnabledLanguages);
            Assert.Equal("auto", result.DefaultLanguage);
        }
        Assert.Equal("auto", JsonSerializer.Deserialize<UserSettings>("{}")!.WebPlayerLanguages.DefaultLanguage);
    }

    [Fact]
    public void PolicyRejectsUnknownCodesAndDisabledDefaultsAndCopiesArrays()
    {
        var codes = new[] { "ja", "ja", "unknown", "zh-TW" };
        var result = WebPlayerLanguageOptions.Normalize(new() { EnabledLanguages = codes, DefaultLanguage = "en" });
        codes[0] = "en";
        Assert.Equal(new[] { "zh-TW", "ja" }, result.EnabledLanguages);
        Assert.Equal("auto", result.DefaultLanguage);
        var fixedDefault = WebPlayerLanguageOptions.Normalize(new() { EnabledLanguages = ["ja"], DefaultLanguage = "ja" });
        Assert.Equal("ja", fixedDefault.DefaultLanguage);
    }
}
