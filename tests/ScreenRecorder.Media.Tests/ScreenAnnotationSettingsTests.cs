// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class ScreenAnnotationSettingsTests
{
    [Fact] public void MissingLegacySettingsEnableAnnotationsAndInvalidWidthsNormalize()
    {
        var settings=System.Text.Json.JsonSerializer.Deserialize<UserSettings>("{}")!;
        Assert.True(settings.ScreenAnnotationsEnabled);
        settings.ScreenAnnotationWidth=double.NaN;Assert.Equal(4,settings.ScreenAnnotationWidth);
        settings.ScreenAnnotationWidth=1000;Assert.Equal(8,settings.ScreenAnnotationWidth);
    }
    [AvaloniaFact] public async Task OptOutAndAppearanceSurviveUnrelatedPreferenceSaves()
    {
        var service=new MockSettingsService{CurrentSettings=new(){ScreenAnnotationsEnabled=false,ScreenAnnotationColor=0xff16a085,ScreenAnnotationWidth=8}};
        var settings=new SettingsViewModel(service);await settings.LoadSettingsAsync();
        Assert.False(settings.ScreenAnnotationsEnabled);await settings.SaveAsync();Assert.False(service.CurrentSettings.ScreenAnnotationsEnabled);
        var main=new MainViewModel(forScreenshot:true);
        typeof(MainViewModel).GetField("_settingsService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,service);
        try{main.ApplyNewSettings(service.CurrentSettings);main.SelectedFps=30;await main.FlushPendingUserSettingsAsync();
            Assert.False(service.CurrentSettings.ScreenAnnotationsEnabled);Assert.Equal(8,service.CurrentSettings.ScreenAnnotationWidth);
            Assert.Equal(0xff16a085u,service.CurrentSettings.ScreenAnnotationColor);
        }finally{main.Cleanup();}
    }
    [AvaloniaTheory] [InlineData(AppLanguage.ZhTw)] [InlineData(AppLanguage.EnUs)] [InlineData(AppLanguage.JaJp)]
    public void EntryIsLocalizedAndOnlyAvailableWhileRecording(AppLanguage language)
    {
        var main=new MainViewModel(forScreenshot:true);var old=main.Strings.CurrentLanguage;
        var window=new MainWindow{DataContext=main};
        try{main.Strings.CurrentLanguage=language;window.Show();window.UpdateLayout();
            var button=window.FindControl<Button>("ScreenAnnotationsButton");Assert.NotNull(button);Assert.False(button.IsEnabled);
            main.IsRecording=true;window.UpdateLayout();Assert.True(button.IsEnabled);
            main.ScreenAnnotationsEnabled=false;window.UpdateLayout();Assert.False(button.IsEffectivelyVisible);
            foreach(var key in new[]{"AnnotationsTitle","AnnotationsEnabled","AnnotationsPen","AnnotationsEsc","AnnotationsCaptured"})
                Assert.NotEqual(key,main.Strings[key]);
        }finally{window.Close();main.Strings.CurrentLanguage=old;main.Cleanup();}
    }
}
