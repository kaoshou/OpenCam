using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Preflight;
using ScreenRecorder.UI.Localization;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioBalanceUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task DelayedTrialAcknowledgementCannotAcceptVolumeEdits()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new PreflightCoordinator(new(), () => new Session());
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var window = new RecordingPreflightWindow(new(), async (command, request, ct) =>
        {
            var reply = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            if (command == "RecordPreflightTrial") await release.Task.WaitAsync(ct);
            return reply;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            window.Show();
            while (!window.FindControl<Button>("TrialButton")!.IsEnabled) await Task.Delay(20, timeout.Token);
            var slider = window.FindControl<Slider>("SystemVolume")!;
            window.FindControl<Button>("TrialButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(slider.IsEnabled);
            // A queued/programmatic value event must not alter an in-flight trial.
            slider.Value = 35;
            release.TrySetResult();
            while (!window.FindControl<Button>("ReplayButton")!.IsEnabled) await Task.Delay(20, timeout.Token);
            window.FindControl<Button>("StartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(100, (await window.Confirmation.WaitAsync(timeout.Token))!.SystemAudioVolumePercent);
        }
        finally { release.TrySetResult(); window.SetPreparationState(RecordingPreparationState.Started); window.Close(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.JaJp)]
    public async Task ChangingVolumeInvalidatesTrialAndConfirmationUsesLatestBalance(AppLanguage language)
    {
        var strings = LanguageManager.Instance; var old = strings.CurrentLanguage; strings.CurrentLanguage = language;
        var lease = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(lease, () => new Session());
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var original = new RecordingConfiguration();
        var window = new RecordingPreflightWindow(original, (command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(20, timeout.Token); }
        void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show();
            await Until(() => window.FindControl<Button>("TrialButton")!.IsEnabled);
            var system = window.FindControl<Slider>("SystemVolume")!;
            var mic = window.FindControl<Slider>("MicrophoneVolume")!;
            Assert.NotNull(system); Assert.NotNull(mic);
            Assert.Equal(100, system.Value);
            Click("TrialButton");
            await Until(() => window.FindControl<Button>("ReplayButton")!.IsEnabled);
            system.Value = 40; mic.Value = 20; system.Value = 0;
            Assert.Equal(0, window.FindControl<ProgressBar>("SystemMeter")!.Value);
            system.Value = 35;
            Assert.False(window.FindControl<Button>("StartButton")!.IsEnabled);
            await Until(() => window.FindControl<Button>("TrialButton")!.IsEnabled);
            Assert.False(window.FindControl<Button>("ReplayButton")!.IsEnabled);
            Assert.Equal(100, original.SystemAudioVolumePercent);
            Assert.NotEqual("PreflightVolumeChanged", window.FindControl<TextBlock>("VolumeNotice")!.Text);
            if (Environment.GetEnvironmentVariable("OPENCAM_REAL_UI_RENDER") == "1" &&
                Environment.GetEnvironmentVariable("OPENCAM_PREFLIGHT_UI_ARTIFACT") is { Length: > 0 } artifact)
            {
                window.UpdateLayout();
                using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(740, 610), new Avalonia.Vector(96, 96));
                image.Render(window); image.Save(artifact + "-" + language + ".png");
            }
            Click("StartButton");
            var accepted = await window.Confirmation.WaitAsync(timeout.Token);
            Assert.NotNull(accepted); Assert.Equal(35, accepted.SystemAudioVolumePercent); Assert.Equal(20, accepted.MicrophoneVolumePercent);
            Assert.False(lease.IsOwned);
        }
        finally { window.SetPreparationState(RecordingPreparationState.Started); window.Close(); strings.CurrentLanguage = old; }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task CancelPendingVolumeUpdateKeepsOriginalAndReleasesCapture()
    {
        var lease = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(lease, () => new Session());
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var original = new RecordingConfiguration { AudioSource = AudioSourceType.MicrophoneOnly };
        var window = new RecordingPreflightWindow(original, (command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        try
        {
            window.Show();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!window.FindControl<Button>("TrialButton")!.IsEnabled) await Task.Delay(20, timeout.Token);
            Assert.False(window.FindControl<Slider>("SystemVolume")!.IsEnabled);
            window.FindControl<Slider>("MicrophoneVolume")!.Value = 0;
            window.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(await window.Confirmation.WaitAsync(timeout.Token));
            Assert.Equal(100, original.MicrophoneVolumePercent); Assert.False(lease.IsOwned);
        }
        finally { window.SetPreparationState(RecordingPreparationState.Started); window.Close(); }
    }

    private sealed class Session : IPreflightLiveSession, IPreflightTrialSession
    {
        private bool _trial, _playing;
        public Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct) => Task.CompletedTask;
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty with
        { Phase = _trial ? PreflightPhase.Reviewing : PreflightPhase.Checking, AudioReady = true, HasTrial = _trial, Playing = _playing,
            Microphone = new(PreflightSourceState.Live, .1, .2), SystemAudio = new(PreflightSourceState.Live, .1, .2), MixLevel = new(.2, .4, now) };
        public ProjectFrameReply? ReadFrame() => null;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task RecordTrialAsync(CancellationToken ct) { _trial = true; return Task.CompletedTask; }
        public Task PlayTrialAsync(CancellationToken ct) { _playing = true; return Task.CompletedTask; }
        public Task StopReplayAsync(CancellationToken ct) { _playing = false; return Task.CompletedTask; }
    }
}
