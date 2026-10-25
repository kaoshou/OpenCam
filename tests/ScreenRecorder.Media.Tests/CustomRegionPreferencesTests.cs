// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class CustomRegionPreferencesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ClosingWaitsForTheLastCustomRegionSnapshot()
    {
        var service = new HeldSettings(); var vm = new MainViewModel(forScreenshot: true);
        typeof(MainViewModel).GetField("_settingsService",Private)!.SetValue(vm,service);
        typeof(MainViewModel).GetField("_displayService",Private)!.SetValue(vm,new TwoDisplays());
        var window = new MainWindow { DataContext=vm };
        try
        {
            window.Show(); vm.SelectedFps=30; await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.UpdateCustomRegion(3058,90,1050,800); window.Close();
            Assert.True(window.IsVisible); Assert.True(vm.HasPendingUserSettings);
            service.Release.TrySetResult(); await vm.FlushPendingUserSettingsAsync();
            for(var i=0;i<100&&window.IsVisible;i++)await Task.Delay(10);
            Assert.False(window.IsVisible);
            var saved=service.Calls.Last();
            Assert.Equal((3058,90,1050,800),(saved.RegionX,saved.RegionY,saved.RegionWidth,saved.RegionHeight));
        }
        finally { service.Release.TrySetResult(); await vm.FlushPendingUserSettingsAsync(); window.Close(); vm.Cleanup(); }
    }
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task CustomRegionIsSavedAsOneSnapshotAfterEarlierPreferenceWrites()
    {
        var service = new HeldSettings(); var vm = new MainViewModel(forScreenshot: true);
        typeof(MainViewModel).GetField("_settingsService", Private)!.SetValue(vm, service);
        typeof(MainViewModel).GetField("_displayService", Private)!.SetValue(vm, new TwoDisplays());
        try
        {
            vm.SelectedFps = 30; await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.UpdateCustomRegion(3058, 90, 1050, 800);
            await Task.Delay(100);
            Assert.Equal(1, service.Calls.Count);
            service.Release.TrySetResult();
            for(var i=0;i<100&&service.Calls.Count<2;i++)await Task.Delay(10);
            var saved = Assert.Single(service.Calls.Skip(1));
            Assert.Equal("CustomRegion", saved.CaptureRangeMode);
            Assert.Equal((3058,90,1050,800),(saved.RegionX,saved.RegionY,saved.RegionWidth,saved.RegionHeight));
        }
        finally { service.Release.TrySetResult(); vm.Cleanup(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RestoredCustomRegionKeepsItsMonitorAndCanResolveWithoutOpeningSelector()
    {
        var vm = new MainViewModel(forScreenshot: true); var displays = new TwoDisplays();
        typeof(MainViewModel).GetField("_displayService", Private)!.SetValue(vm, displays);
        typeof(MainViewModel).GetField("_settingsService", Private)!.SetValue(vm,
            new MockSettingsService { CurrentSettings = new() { CaptureRangeMode="CustomRegion", SelectedMonitorIndex=1,
                RegionX=3058,RegionY=90,RegionWidth=1050,RegionHeight=800 } });
        vm.AvailableMonitors.Clear();
        foreach(var m in displays.GetMonitors())vm.AvailableMonitors.Add(new(m.Index,m.DeviceName,m.Bounds.Width,m.Bounds.Height,m.IsPrimary));
        try
        {
            typeof(MainViewModel).GetMethod("LoadUserSettings",Private)!.Invoke(vm,null);
            Assert.True(vm.IsCustomRegion); Assert.Equal(1,vm.SelectedMonitor!.Index);
            var config=vm.BuildProjectConfiguration();
            Assert.Equal(new CaptureRegion(3058,90,1050,800),config.Region);
            Assert.NotNull(ObsRecordingEngineRouter.ResolveMonitor(config,config.Region,null,displays.GetMonitors(),_=>"native-display"));
        }
        finally { vm.Cleanup(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(3058,90,1050,1192)]
    [InlineData(9999,90,1050,800)]
    [InlineData(2500,90,1050,800)]
    public async Task InvalidRestoredRegionFailsBeforeStartingAudioPreflight(int x,int y,int width,int height)
    {
        var vm=new MainViewModel(forScreenshot:true);
        typeof(MainViewModel).GetField("_displayService",Private)!.SetValue(vm,new TwoDisplays());
        var preflights=0; vm.RequestRecordingPreflight=(config,_)=>{preflights++;return Task.FromResult<RecordingConfiguration?>(config);};
        try
        {
            var config=new RecordingConfiguration { CaptureSource=CaptureSourceType.CustomRegion,
                Region=new(x,y,width,height),AudioSource=AudioSourceType.SystemAndMicrophone };
            var task=(Task<RecordingConfiguration?>)typeof(MainViewModel).GetMethod("CheckBeforeRecordingAsync",Private)!.Invoke(vm,[config,false])!;
            Assert.Null(await task); Assert.Equal(0,preflights);
            Assert.Equal(vm.Strings["StatusRegionNeedsReselection"],vm.StatusMessage);
        }
        finally { vm.Cleanup(); }
    }

    private sealed class TwoDisplays : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors()=>[new(0,"primary",new(0,0,2560,1600),true,1.5),new(1,"secondary",new(2560,0,1920,1080),false,1)];
        public MonitorInfo? GetPrimaryMonitor()=>GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds()=>new(0,0,4480,1600);
    }
    private sealed class HeldSettings : ISettingsService
    {
        public List<UserSettings> Calls=[];
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UserSettings> LoadSettingsAsync(CancellationToken ct=default)=>Task.FromResult(new UserSettings());
        public async Task SaveSettingsAsync(UserSettings settings,CancellationToken ct=default)
        {
            lock(Calls)Calls.Add(settings);
            Started.TrySetResult(); await Release.Task;
        }
    }
}
