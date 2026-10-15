// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Settings;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public class MicrophoneReminderUiTests
{
    [AvaloniaFact]
    public void WarningOnlyChangesAdvisoryStateAndClearsOnPauseStopOrDisable()
    {
        var vm = new MainViewModel(forScreenshot: true) { RecordMicrophone = true, IsRecording = true };
        try
        {
            var silent = new AudioLevelsSnapshot(new(AudioMeterState.Live, 0.8, 0.9), new(AudioMeterState.Silent, 0, 0));
            void Warn()
            {
                for (var second = 0; second <= 30; second++)
                    vm.ApplyAudioLevels(silent, TimeSpan.FromSeconds(second));
                Assert.True(vm.IsMicrophoneSilenceWarningActive);
                Assert.True(vm.IsMicrophoneSilenceInAppWarningVisible);
                Assert.True(vm.IsRecording);
                Assert.False(vm.IsPaused);
                Assert.True(vm.CanStopRecording);
            }
            Warn();
            vm.MicrophoneWarningOverlayVisible = true;
            Assert.False(vm.IsMicrophoneSilenceInAppWarningVisible);
            vm.MicrophoneWarningOverlayVisible = false;
            Assert.True(vm.IsMicrophoneSilenceInAppWarningVisible);
            vm.IsPaused = true;
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
            vm.IsPaused = false;
            Warn();
            vm.MicrophoneSilenceReminderEnabled = false;
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
            vm.MicrophoneSilenceReminderEnabled = true;
            Warn();
            vm.RecordMicrophone = false;
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
            vm.RecordMicrophone = true;
            Warn();
            vm.IsRecording = false;
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
        }
        finally { vm.Cleanup(); }
    }

    [AvaloniaFact]
    public void DisabledMicrophoneAndMissingDataDoNotWarn()
    {
        var vm = new MainViewModel(forScreenshot: true) { IsRecording = true };
        try
        {
            for (var second = 0; second <= 40; second++)
                vm.ApplyAudioLevels(new(new(AudioMeterState.Live, 1, 1), new(AudioMeterState.Silent, 0, 0)), TimeSpan.FromSeconds(second));
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
            vm.RecordMicrophone = true;
            for (var second = 0; second <= 40; second++)
                vm.ApplyAudioLevels(new(new(AudioMeterState.Silent, 0, 0), new(AudioMeterState.Unavailable, 0, 0)), TimeSpan.FromSeconds(second));
            Assert.False(vm.IsMicrophoneSilenceWarningActive);
            Assert.True(vm.IsRecording);
        }
        finally { vm.Cleanup(); }
    }

    [Fact]
    public async Task PreferenceDefaultsOnAndSurvivesSaveReloadAndReset()
    {
        var root = Directory.CreateTempSubdirectory("opencam-silence-settings-");
        try
        {
            var path = Path.Combine(root.FullName, "settings.json");
            // Existing user settings have no reminder key; migration must default on.
            await File.WriteAllTextAsync(path, "{\"Language\":0,\"MinimizeOnRecord\":true}");
            var vm = new SettingsViewModel(new SettingsService(path));
            await vm.LoadSettingsAsync();
            Assert.True(vm.MicrophoneSilenceReminderEnabled);
            vm.MicrophoneSilenceReminderEnabled = false;
            await vm.SaveAsync();
            var restored = new SettingsViewModel(new SettingsService(path));
            await restored.LoadSettingsAsync();
            Assert.False(restored.MicrophoneSilenceReminderEnabled);
            Assert.True(restored.MinimizeOnRecord);
            restored.ResetToDefaults();
            await restored.SaveAsync();
            Assert.True((await new SettingsService(path).LoadSettingsAsync()).MicrophoneSilenceReminderEnabled);
        }
        finally { root.Delete(recursive: true); }
    }
}
