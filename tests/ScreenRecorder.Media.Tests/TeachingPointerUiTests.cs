using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class TeachingPointerUiTests
{
    [AvaloniaFact]
    public void FailedOptionalThumbnailDoesNotPreventOrdinaryRecordingConfiguration()
    {
        var cache=(System.Collections.IDictionary)typeof(MainViewModel)
            .GetField("PointerThumbnails",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var key=TeachingPointerStyle.WarmIvory;
        var previous=cache[key];
        var failed=new Lazy<Avalonia.Media.Imaging.Bitmap>(()=>throw new System.IO.InvalidDataException("Invalid optional thumbnail"));
        cache[key]=failed;
        MainViewModel? vm=null;
        try
        {
            vm=new MainViewModel(forScreenshot:true);
            Assert.Equal(4,vm.AvailableTeachingPointerStyles.Count);
            Assert.Null(vm.AvailableTeachingPointerStyles.Single(x=>x.Style==key).Thumbnail);
            Assert.NotSame(failed,cache[key]); // A transient failure must not poison later windows.
            Assert.Equal(CursorEffectMode.Default,vm.BuildProjectConfiguration().CursorEffect);
            Assert.Equal(TeachingPointerOptions.Default,vm.BuildProjectConfiguration().PresentationPointer);
        }
        finally
        {
            vm?.Cleanup();
            if(previous is null)cache.Remove(key);else cache[key]=previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.JaJp)]
    public void TeachingPointerIsSecondWithoutRenumberingStoredModes(AppLanguage language)
    {
        var vm=new MainViewModel(forScreenshot:true); var old=vm.Strings.CurrentLanguage;
        try
        {
            vm.Strings.CurrentLanguage=language;
            Assert.Equal(new[]{CursorEffectMode.Default,CursorEffectMode.PresentationStick,CursorEffectMode.HighlightHalo,
                CursorEffectMode.HaloWithClickRipple,CursorEffectMode.Hidden},vm.AvailableCursorEffects.Select(x=>x.Mode));
            vm.SelectedCursorEffect=vm.AvailableCursorEffects[1];
            vm.Strings.CurrentLanguage=language==AppLanguage.JaJp?AppLanguage.EnUs:AppLanguage.JaJp;
            Assert.Equal(CursorEffectMode.PresentationStick,vm.SelectedCursorEffect!.Mode);
        }
        finally {vm.Strings.CurrentLanguage=old;vm.Cleanup();}
    }

    [AvaloniaFact]
    public void TeachingStyleAndSizeControlsAreReachableOnlyForStickAndLockDuringRecording()
    {
        var vm=new MainViewModel(forScreenshot:true);
        var window=new MainWindow{DataContext=vm};
        try
        {
            window.Show();window.UpdateLayout();
            var style=window.FindControl<ComboBox>("TeachingPointerStylePicker");
            var size=window.FindControl<ComboBox>("TeachingPointerSizePicker");
            Assert.NotNull(style);Assert.NotNull(size);
            Assert.False(style.IsEffectivelyVisible);
            vm.SelectedCursorEffect=vm.AvailableCursorEffects.Single(x=>x.Mode==CursorEffectMode.PresentationStick);
            window.UpdateLayout();
            Assert.True(style.IsEffectivelyVisible);
            Assert.Equal(4,style.ItemCount);Assert.Equal(3,size.ItemCount);
            vm.IsRecording=true;window.UpdateLayout();
            Assert.False(style.IsEnabled);Assert.False(size.IsEnabled);
            vm.IsRecording=false;vm.IsPaused=true;window.UpdateLayout();
            Assert.True(style.IsEnabled);Assert.True(size.IsEnabled);
        }
        finally {window.Close();vm.Cleanup();}
    }

    [AvaloniaFact]
    public async Task SelectionsPersistAcrossLanguageChangesAndUnrelatedSettingsSaves()
    {
        var service=new MockSettingsService();
        var vm=new MainViewModel(forScreenshot:true);
        var original=vm.Strings.CurrentLanguage;
        typeof(MainViewModel).GetField("_settingsService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
            .SetValue(vm,service);
        try
        {
            Assert.Equal(TeachingPointerStyle.WarmIvory,vm.SelectedPresentationPointerStyle!.Style);
            Assert.Equal(TeachingPointerSize.Standard,vm.SelectedPresentationPointerSize!.Size);
            Assert.All(vm.AvailableTeachingPointerStyles,x=>Assert.NotNull(x.Thumbnail));
            var options=new TeachingPointerOptions(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large);
            vm.ApplyNewSettings(new UserSettings{PresentationPointer=options});
            vm.SelectedCursorEffect=vm.AvailableCursorEffects.Single(x=>x.Mode==CursorEffectMode.PresentationStick);
            vm.Strings.CurrentLanguage=AppLanguage.JaJp;
            Assert.Equal(options,vm.BuildProjectConfiguration().PresentationPointer);
            Assert.Equal("高コントラスト・ゴールド",vm.SelectedPresentationPointerStyle!.DisplayName);
            vm.Strings.CurrentLanguage=AppLanguage.EnUs;
            Assert.Equal(options,vm.BuildProjectConfiguration().PresentationPointer);
            Assert.Equal("High-contrast gold",vm.SelectedPresentationPointerStyle!.DisplayName);
            vm.SelectedFps=30;
            for(var i=0;i<100&&service.CurrentSettings.PresentationPointer!=options;i++)await Task.Delay(10);
            Assert.Equal(options,service.CurrentSettings.PresentationPointer);
            var settings=new SettingsViewModel(service);
            await settings.LoadSettingsAsync();
            await settings.SaveCommand.ExecuteAsync(null);
            Assert.Equal(options,service.CurrentSettings.PresentationPointer);
            var second=new MainViewModel(forScreenshot:true);
            try {second.ApplyNewSettings(service.CurrentSettings);Assert.Equal(options,second.BuildProjectConfiguration().PresentationPointer);}
            finally {second.Cleanup();}
        }
        finally{vm.Strings.CurrentLanguage=original;vm.Cleanup();}
    }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.JaJp)]
    public void TeachingSelectorsFitNarrowMainWindowAndKeepSelectedStyle(AppLanguage language)
    {
        var vm=new MainViewModel(forScreenshot:true);var original=vm.Strings.CurrentLanguage;
        var window=new MainWindow{DataContext=vm,Width=820,Height=820};
        try
        {
            vm.Strings.CurrentLanguage=language;
            vm.SelectedCursorEffect=vm.AvailableCursorEffects.Single(x=>x.Mode==CursorEffectMode.PresentationStick);
            window.Show();window.UpdateLayout();
            foreach(var name in new[]{"TeachingPointerStylePicker","TeachingPointerSizePicker"})
            {
                var picker=window.FindControl<ComboBox>(name)!;
                Assert.True(picker.IsEffectivelyVisible);
                Assert.True(picker.Bounds.Width>150);
                Assert.True(picker.Bounds.Height>=28);
                Assert.NotNull(picker.SelectedItem);
            }
            var artifact=Environment.GetEnvironmentVariable("OPENCAM_TEACHING_UI_ARTIFACT");
            if(string.IsNullOrEmpty(artifact))
                artifact=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OpenCam-teaching-main-layout");
            if(Environment.GetEnvironmentVariable("OPENCAM_REAL_UI_RENDER")=="1")
            {
                // A raster-DPI change is not a simulated display-DPI change. Keep this
                // capture at 1x; mixed-DPI/Retina remains physical-device validation.
                using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(820,820),new Avalonia.Vector(96,96));
                image.Render(window);
                image.Save(artifact+"-"+language+"-1x.png");
                Assert.True(System.IO.File.Exists(artifact+"-"+language+"-1x.png"));
            }
        }
        finally{window.Close();vm.Strings.CurrentLanguage=original;vm.Cleanup();}
    }
}
