// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    private readonly MicrophoneSilenceReminder _microphoneSilenceReminder = new();
    [ObservableProperty] private bool _microphoneSilenceReminderEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMicrophoneSilenceInAppWarningVisible))]
    private bool _isMicrophoneSilenceWarningActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMicrophoneSilenceInAppWarningVisible))]
    private bool _microphoneWarningOverlayVisible;
    public bool IsMicrophoneSilenceInAppWarningVisible => IsMicrophoneSilenceWarningActive && !MicrophoneWarningOverlayVisible;

    partial void OnMicrophoneSilenceReminderEnabledChanged(bool value)
    {
        ResetMicrophoneSilenceReminder();
    }

    private void ResetMicrophoneSilenceReminder()
    {
        _microphoneSilenceReminder.Reset();
        IsMicrophoneSilenceWarningActive = false;
    }

    internal void ApplyAudioLevels(AudioLevelsSnapshot snapshot, TimeSpan elapsed)
    {
        _systemMeterState = snapshot.SystemAudio.State;
        _microphoneMeterState = snapshot.Microphone.State;
        _systemWaveform.Push(snapshot.SystemAudio);
        _microphoneWaveform.Push(snapshot.Microphone);
        IsMicrophoneSilenceWarningActive = _microphoneSilenceReminder.Update(
            MicrophoneSilenceReminderEnabled && IsRecording && !IsPaused && RecordMicrophone,
            snapshot.Microphone.State, elapsed);
        NotifyAudioMeterChanged();
    }
}
