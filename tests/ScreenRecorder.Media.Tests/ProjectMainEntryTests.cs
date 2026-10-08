// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMainEntryTests
{
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
