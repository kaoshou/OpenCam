// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Tests;
public sealed class LinuxSourceSelectionUiTests
{
    private static LinuxPortalSelectionView Source(){var rgba=new byte[480*270*4];rgba[3]=255;return new("portal:"+new string('a',64),1920,1080,480,270,Convert.ToBase64String(rgba));}
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AuthorizedSelectionResetsStaleDesktopCoordinatesWithoutRefreshingWindow()
    {
        var vm=new MainViewModel(forScreenshot:true);
        typeof(MainViewModel).GetField("_displayService",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,new LinuxDesktopCatalog());
        try {
            vm.UpdateCustomRegion(3058,90,1050,800);
            vm.ApplyLinuxPortalSelection(Source());
            Assert.Single(vm.AvailableMonitors);
            Assert.Equal(0,vm.SelectedMonitor!.Index);
            Assert.True(vm.IsMonitorSelected);
            Assert.Equal((0,0,1920,1080),(vm.RegionX,vm.RegionY,vm.RegionWidth,vm.RegionHeight));
        }finally{vm.Cleanup();}
    }
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CropWindowClampsInitialSelectionToAuthorizedSource()
    {
        var window=new LinuxPortalCropWindow(Source(),new(3058,90,1050,800));
        try{window.Show();Assert.Equal(new CaptureRegion(0,0,1920,1080),window.SelectedRegion);}
        finally{window.Close();}
    }
}
