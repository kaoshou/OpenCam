using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.Windows.Capture;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class CapturePreferenceCleanupTests
{
    [Fact]
    public async Task StaleWindowsWgcFlagCannotOverrideCompatibleCapture()
    {
        var compatible=new WindowsCapturePlanProvider(new WindowsFFmpegProvider(),new EmptyCatalog(),()=>[],_=>Task.FromResult(false));
        var probes=0;
        var provider=new WindowsGraphicsCapturePlan(compatible,_=>{probes++;return Task.FromResult(true);},
            b=>new(123,"screen",b));
        var config=new RecordingConfiguration{NativeCaptureEnabled=true,Fps=30,AudioSource=AudioSourceType.SystemAndMicrophone};
        var bounds=new CaptureRegion(0,0,640,480);
        var plan=await provider.PrepareAsync(config,bounds,null,default);
        Assert.Equal(CaptureBackend.Gdi,plan.Selection.Backend);
        Assert.False(config.NativeCaptureEnabled);
        Assert.Equal(0,probes);
        Assert.Contains("-framerate 30",plan.VideoInputArguments);
        Assert.Equal(AudioSourceType.SystemAndMicrophone,config.AudioSource);
        var pinned=plan.Selection with{Backend=CaptureBackend.WindowsGraphicsCapture};
        await Assert.ThrowsAsync<NotSupportedException>(()=>provider.PrepareAsync(config,bounds,pinned,default));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.PrepareAsync(config,bounds,null,cancelled.Token));
    }
    private sealed class EmptyCatalog:IDxgiOutputCatalog
    { public IReadOnlyList<DxgiOutputInfo> GetOutputs()=>[]; }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.JaJp)]
    public async Task WindowsHasOneCapturePickerAndNoHighPerformanceToggle(AppLanguage language)
    {
        var vm=new SettingsViewModel(new MockSettingsService{CurrentSettings=new(){NativeCaptureEnabled=true}},true);
        var old=vm.Strings.CurrentLanguage;
        var window=new SettingsWindow{DataContext=vm};
        try
        {
            await vm.LoadSettingsAsync();vm.Strings.CurrentLanguage=language;
            window.Show();window.UpdateLayout();
            Assert.False(vm.NativeCaptureEnabled);
            var picker=window.FindControl<ComboBox>("CaptureModePicker");
            Assert.NotNull(picker);Assert.True(picker.IsEffectivelyVisible);Assert.Equal(3,picker.ItemCount);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(),
                c=>c.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==vm.Strings["NativeCaptureLabel"]));
        }
        finally {window.Close();vm.Strings.CurrentLanguage=old;}
    }

    [MacOsAvaloniaFact]
    public async Task MacCaptureDropdownPreservesScreenCaptureKitAcrossLanguageAndLockedSave()
    {
        var service=new MockSettingsService{CurrentSettings=new(){NativeCaptureEnabled=true}};
        var vm=new SettingsViewModel(service,false);
        var old=vm.Strings.CurrentLanguage;
        try
        {
            await vm.LoadSettingsAsync();
            Assert.True(vm.SupportsNativeCapture);
            Assert.True(vm.SupportsCaptureSelection);
            Assert.Equal(2,vm.AvailableCaptureModes.Count);
            Assert.Equal(WindowsCaptureMode.ModernExperimental,vm.SelectedCaptureMode!.Mode);
            vm.Strings.CurrentLanguage=AppLanguage.JaJp;
            Assert.Equal(WindowsCaptureMode.ModernExperimental,vm.SelectedCaptureMode!.Mode);
            await vm.SaveAsync();Assert.True(service.CurrentSettings.NativeCaptureEnabled);
            vm.CanEditCaptureMode=false;
            vm.SelectedCaptureMode=vm.AvailableCaptureModes.Single(o=>o.Mode==WindowsCaptureMode.CompatibleGdi);
            await vm.SaveAsync();Assert.True(service.CurrentSettings.NativeCaptureEnabled);
            vm.CanEditCaptureMode=true;
            await vm.SaveAsync();Assert.False(service.CurrentSettings.NativeCaptureEnabled);
            Assert.Equal((WindowsCaptureMode)2,service.CurrentSettings.WindowsCaptureMode);
        }
        finally {vm.Strings.CurrentLanguage=old;}
    }
}
