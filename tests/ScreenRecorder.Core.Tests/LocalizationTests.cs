using ScreenRecorder.Core.Localization;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class LocalizationTests
{
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
