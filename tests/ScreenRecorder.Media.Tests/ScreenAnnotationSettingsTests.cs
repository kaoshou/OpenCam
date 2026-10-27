// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;
using ScreenRecorder.Infrastructure.Settings;

namespace ScreenRecorder.Media.Tests;

public class ScreenAnnotationSettingsTests
{
    [Fact] public void DefaultShortcutsAreDistinctAndFollowTheRequestedRecordingKeys()
    {
        var settings=new UserSettings();
        Assert.Equal((2u,"F9"),(settings.StartStopHotkeyModifiers,settings.StartStopHotkeyKey));
        Assert.Equal((0u,"F9"),(settings.PauseResumeHotkeyModifiers,settings.PauseResumeHotkeyKey));
        Assert.Equal("F10",settings.AnnotationShortcuts["togglePen"].Key);
        Assert.Equal("F11",settings.AnnotationShortcuts["toggleToolbar"].Key);
        Assert.Equal("Escape",settings.AnnotationShortcuts["finish"].Key);
        Assert.Equal(settings.AnnotationShortcuts.Count,settings.AnnotationShortcuts.Values.Select(x=>(x.Modifiers,x.Key)).Distinct().Count());
    }
    [Fact] public void MissingLegacySettingsEnableAnnotationsAndInvalidWidthsNormalize()
    {
        var settings=System.Text.Json.JsonSerializer.Deserialize<UserSettings>("{}")!;
        Assert.True(settings.ScreenAnnotationsEnabled);
        settings.ScreenAnnotationWidth=double.NaN;Assert.Equal(4,settings.ScreenAnnotationWidth);
        settings.ScreenAnnotationWidth=1000;Assert.Equal(8,settings.ScreenAnnotationWidth);
    }
    [Fact] public async Task LegacyPauseShortcutIsPreservedAndAnnotationUsesAnotherShortcut()
    {
        var path=Path.Combine(Path.GetTempPath(),"opencam-hotkey-test-"+Guid.NewGuid()+".json");
        try
        {
            await File.WriteAllTextAsync(path,"{\"StartStopHotkeyKey\":\"F9\",\"PauseResumeHotkeyKey\":\"F10\"}");
            var settings=await new SettingsService(path).LoadSettingsAsync();
            Assert.Equal("F9",settings.StartStopHotkeyKey);Assert.Equal("F10",settings.PauseResumeHotkeyKey);
            Assert.Equal(0u,settings.StartStopHotkeyModifiers);
            Assert.Equal((6u,"F8"),(settings.AnnotationShortcuts["togglePen"].Modifiers,settings.AnnotationShortcuts["togglePen"].Key));
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(0u,"F11")]
    [InlineData(6u,"F1")]
    [InlineData(6u,"F2")]
    [InlineData(6u,"F3")]
    [InlineData(6u,"F4")]
    public async Task LegacyRecordingShortcutsKeepTheirKeysAndAllAnnotationDefaultsAvoidCollisions(uint modifiers,string key)
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            await File.WriteAllTextAsync(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                StartStopHotkeyModifiers=modifiers,StartStopHotkeyKey=key,
                PauseResumeHotkeyModifiers=0,PauseResumeHotkeyKey="F10",
                BookmarkHotkeyModifiers=6,BookmarkHotkeyKey="F8"
            }));
            var service=new SettingsService(path);var settings=await service.LoadSettingsAsync();
            Assert.Equal((modifiers,key),(settings.StartStopHotkeyModifiers,settings.StartStopHotkeyKey));
            Assert.Equal((0u,"F10"),(settings.PauseResumeHotkeyModifiers,settings.PauseResumeHotkeyKey));
            Assert.Equal((6u,"F8"),(settings.BookmarkHotkeyModifiers,settings.BookmarkHotkeyKey));
            var bindings=settings.AnnotationShortcuts.Values.Select(x=>(x.Modifiers,x.Key.ToUpperInvariant()))
                .Concat(new[]{(modifiers,key.ToUpperInvariant()),(0u,"F10"),(6u,"F8")}).ToArray();
            Assert.Equal(bindings.Length,bindings.Distinct().Count());
            Assert.DoesNotContain(settings.AnnotationShortcuts.Values,x=>x.Key=="F12");
            await service.SaveSettingsAsync(settings);var reloaded=await service.LoadSettingsAsync();
            foreach(var (id,binding) in settings.AnnotationShortcuts)
                Assert.Equal((binding.Modifiers,binding.Key),(reloaded.AnnotationShortcuts[id].Modifiers,reloaded.AnnotationShortcuts[id].Key));
        }
        finally {File.Delete(path);}
    }
    [Fact] public async Task AnnotationShortcutsCanBeChangedAndDuplicateKeysAreRejected()
    {
        var service=new MockSettingsService();var vm=new SettingsViewModel(service);await vm.LoadSettingsAsync();
        var pen=vm.AnnotationShortcutRows.Single(x=>x.Id=="togglePen");
        pen.SelectedKey=pen.Keys.Single(x=>x.KeyName=="F11");
        await vm.SaveAsync();Assert.NotNull(vm.ErrorMessage);
        pen.SelectedKey=pen.Keys.Single(x=>x.KeyName=="F7");
        await vm.SaveAsync();Assert.Null(vm.ErrorMessage);Assert.Equal("F7",service.CurrentSettings.AnnotationShortcuts["togglePen"].Key);
    }
    [AvaloniaFact] public async Task ShortcutRowsAreActuallyShownInSettingsWindow()
    {
        var vm=new SettingsViewModel(new MockSettingsService());await vm.LoadSettingsAsync();
        var window=new SettingsWindow{DataContext=vm};
        try
        {
            window.Show();window.UpdateLayout();
            var group=window.FindControl<Expander>("AnnotationShortcutsExpander");
            Assert.NotNull(group);Assert.False(group.IsExpanded);
            group.IsExpanded=true;window.UpdateLayout();
            Assert.True(group.Bounds.Width>=window.ClientSize.Width-100);
            var row=vm.AnnotationShortcutRows.Single(x=>x.Id=="togglePen");
            Assert.Contains(window.GetVisualDescendants().OfType<ComboBox>(),x=>ReferenceEquals(x.DataContext,row));
        }
        finally {window.Close();}
    }
    [AvaloniaTheory] [InlineData(AppLanguage.ZhTw,"無")] [InlineData(AppLanguage.EnUs,"None")] [InlineData(AppLanguage.JaJp,"なし")]
    public void NoModifierLabelMatchesSelectedLanguage(AppLanguage language,string expected)
    {
        var old=ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage;
        try { ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage=language;
            var vm=new SettingsViewModel(new MockSettingsService());Assert.Equal(expected,vm.AvailableModifiers.Single(x=>x.Value==0).DisplayName);
        } finally {ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage=old;}
    }
    [AvaloniaFact] public async Task LanguageChangeRefreshesModifierLabelsWithoutChangingSelectedShortcuts()
    {
        var language=ScreenRecorder.UI.Localization.LanguageManager.Instance;var old=language.CurrentLanguage;
        try
        {
            language.CurrentLanguage=AppLanguage.ZhTw;var vm=new SettingsViewModel(new MockSettingsService());await vm.LoadSettingsAsync();
            var expected=vm.AnnotationShortcutRows.ToDictionary(x=>x.Id,x=>(x.SelectedModifier!.Value,x.SelectedKey!.KeyName));
            language.CurrentLanguage=AppLanguage.JaJp;
            Assert.Equal("なし",vm.AvailableModifiers.Single(x=>x.Value==0).DisplayName);
            foreach(var row in vm.AnnotationShortcutRows)Assert.Equal(expected[row.Id],(row.SelectedModifier!.Value,row.SelectedKey!.KeyName));
            Assert.Equal((2u,"F9"),(vm.SelectedStartStopModifier!.Value,vm.SelectedStartStopKey!.KeyName));
        }
        finally {language.CurrentLanguage=old;}
    }
    [AvaloniaFact] public void ClosedSettingsWindowStopsRefreshingItsViewModelOnLanguageChanges()
    {
        var language=ScreenRecorder.UI.Localization.LanguageManager.Instance;var old=language.CurrentLanguage;
        var vm=new SettingsViewModel(new MockSettingsService());var window=new SettingsWindow(vm);
        try
        {
            window.Show();window.UpdateLayout();window.Close();
            var changes=0;vm.PropertyChanged+=(_,_)=>changes++;
            language.CurrentLanguage=old==AppLanguage.EnUs?AppLanguage.JaJp:AppLanguage.EnUs;
            Assert.Equal(0,changes);
            Assert.Null(window.DataContext);
        }
        finally {window.Close();language.CurrentLanguage=old;}
    }
    [Fact] public async Task WindowsRejectsReservedF12ForGlobalShortcuts()
    {
        var service=new MockSettingsService();var vm=new SettingsViewModel(service,true);await vm.LoadSettingsAsync();
        vm.SelectedStartStopKey=vm.AvailableKeys.Single(key=>key.KeyName=="F12");
        await vm.SaveAsync();Assert.Equal(vm.Strings["HotkeyF12Reserved"],vm.ErrorMessage);Assert.Equal(0,service.SaveCount);
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
