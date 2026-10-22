// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Localization;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void JapaneseSelection_ReturnsJapaneseAndFormatsErrors()
    {
        var service = new LocalizationService { CurrentLanguage = AppLanguage.JaJp };
        Assert.Equal("録画開始", service["StartRecording"]);
        Assert.Equal("開始できませんでした: Disk full", service.GetFormatted("StatusFailed", "Disk full"));
    }

    [Fact]
    public void JapaneseCatalog_CoversEveryUiKeyWithMatchingFormatArguments()
    {
        Assert.Equal(LocalizationService.ZhTwDictionary.Keys.OrderBy(key => key),
            LocalizationService.JaJpDictionary.Keys.OrderBy(key => key));
        foreach (var (key, template) in LocalizationService.ZhTwDictionary)
        {
            var japanese = LocalizationService.JaJpDictionary[key];
            Assert.False(string.IsNullOrWhiteSpace(japanese), key);
            var arguments = System.Text.RegularExpressions.Regex.Matches(template, @"\{\d+(?:[^}]*)\}")
                .Select(match => match.Value).OrderBy(value => value);
            var translatedArguments = System.Text.RegularExpressions.Regex.Matches(japanese, @"\{\d+(?:[^}]*)\}")
                .Select(match => match.Value).OrderBy(value => value);
            Assert.Equal(arguments, translatedArguments);
            Assert.Equal(System.Text.CompositeFormat.Parse(template).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(japanese).MinimumArgumentCount);
        }
    }

    [Theory]
    [InlineData("ActualEncoderPending")]
    [InlineData("ActualEncoderFormat")]
    [InlineData("EncoderFallbackNoHardware")]
    [InlineData("EncoderFallbackRequested")]
    [InlineData("EncoderFallbackStartup")]
    public void EncoderDiagnostics_HaveBilingualKeys(string key)
    {
        Assert.True(LocalizationService.ZhTwDictionary.ContainsKey(key));
        Assert.True(LocalizationService.EnUsDictionary.ContainsKey(key));
    }

    [Fact]
    public void BothDictionaries_ShouldContainIdenticalKeySets()
    {
        var zhKeys = LocalizationService.ZhTwDictionary.Keys.OrderBy(k => k).ToList();
        var enKeys = LocalizationService.EnUsDictionary.Keys.OrderBy(k => k).ToList();

        Assert.NotEmpty(zhKeys);
        Assert.NotEmpty(enKeys);

        var missingInEn = zhKeys.Except(enKeys).ToList();
        var missingInZh = enKeys.Except(zhKeys).ToList();

        Assert.Empty(missingInEn);
        Assert.Empty(missingInZh);
    }

    [Fact]
    public void GetString_ShouldReturnCorrectLanguage()
    {
        var service = new LocalizationService();
        service.CurrentLanguage = AppLanguage.ZhTw;
        Assert.Equal("開始錄影", service["StartRecording"]);

        service.CurrentLanguage = AppLanguage.EnUs;
        Assert.Equal("Record", service["StartRecording"]);
    }

    [Fact]
    public void GetFormatted_ShouldInterpolateValues()
    {
        var service = new LocalizationService();
        service.CurrentLanguage = AppLanguage.ZhTw;
        var zhFormatted = service.GetFormatted("StatusFailed", "磁碟已滿");
        Assert.Equal("啟動失敗: 磁碟已滿", zhFormatted);

        service.CurrentLanguage = AppLanguage.EnUs;
        var enFormatted = service.GetFormatted("StatusFailed", "Disk full");
        Assert.Equal("Failed to start: Disk full", enFormatted);
    }

    [Fact]
    public void OpenFolderFailure_ShouldBeLocalized()
    {
        var service = new LocalizationService();
        service.CurrentLanguage = AppLanguage.ZhTw;
        Assert.Equal(
            "無法開啟儲存位置：Finder error",
            service.GetFormatted("StatusOpenFolderFailed", "Finder error"));

        service.CurrentLanguage = AppLanguage.EnUs;
        Assert.Equal(
            "Unable to open the output location: Finder error",
            service.GetFormatted("StatusOpenFolderFailed", "Finder error"));
    }

    [Fact]
    public void SystemAudioLabel_ShouldBePlatformNeutral()
    {
        var service = new LocalizationService();
        service.CurrentLanguage = AppLanguage.ZhTw;
        Assert.Equal("錄製系統聲音", service["AudioSystem"]);

        service.CurrentLanguage = AppLanguage.EnUs;
        Assert.Equal("Record System Audio", service["AudioSystem"]);
    }

    [Fact]
    public void LanguageChangedEvent_ShouldFireOnSwitch()
    {
        var service = new LocalizationService();
        AppLanguage? received = null;
        service.LanguageChanged += (s, lang) => received = lang;

        service.CurrentLanguage = AppLanguage.EnUs;
        Assert.Equal(AppLanguage.EnUs, received);

        service.CurrentLanguage = AppLanguage.ZhTw;
        Assert.Equal(AppLanguage.ZhTw, received);
    }
}
