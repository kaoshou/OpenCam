// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMainEntryTests
{
    [Fact]
    public void RecordingFrameRateMenuOmitsSixtyAndPassesRemainingChoicesToRecorder()
    {
        var vm = new MainViewModel(forScreenshot: true);
        try
        {
            Assert.Equal(10, vm.SelectedFps);
            Assert.Equal(new[] { 5, 10, 20, 30, 40 }, vm.FpsOptions);
            foreach (var fps in vm.FpsOptions)
            {
                vm.SelectedFps = fps;
                Assert.Equal(fps, vm.BuildProjectConfiguration().Fps);
            }
        }
        finally { vm.Cleanup(); }
    }

    [Theory]
    [InlineData(AppLanguage.ZhTw, "編輯錄製內容")]
    [InlineData(AppLanguage.EnUs, "Edit Recording")]
    public void ProductEntryUsesNormalProjectTitle(AppLanguage language, string expected)
    {
        var localization = new LocalizationService { CurrentLanguage = language };
        Assert.Equal(expected, localization["ProjectWorkspace"]);
    }

    [Fact]
    public void ProjectWorkspaceOwnsRecordingControls_QuickControlsReturnAfterClosing()
    {
        var vm = new MainViewModel(forScreenshot: true);
        vm.IsProjectWorkspaceOpen = true;
        Assert.False(vm.CanStartRecording);
        Assert.False(vm.CanRecoverSessions);
        vm.IsRecording = true;
        Assert.False(vm.CanPauseOrResume);
        Assert.False(vm.CanStopRecording);
        Assert.True(vm.IsApplicationCloseBlocked);
        vm.IsRecording = false;
        vm.IsProjectWorkspaceOpen = false;
        Assert.True(vm.CanStartRecording);
        vm.Cleanup();
    }

    [Fact]
    public void ProjectCaptureUsesExistingSettingsWithoutChangingQuickDefaults()
    {
        var vm = new MainViewModel(forScreenshot: true) { SelectedFps = 24, RecordMicrophone = true };
        var config = vm.BuildProjectConfiguration();
        Assert.Equal(24, config.Fps);
        Assert.Equal(AudioSourceType.MicrophoneOnly, config.AudioSource);
        Assert.Equal(vm.OutputDirectory, config.OutputDirectory);
        vm.Cleanup();
    }
}
