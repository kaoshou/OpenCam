using Avalonia.Controls;
using ScreenRecorder.UI.Preflight;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.Media.Tests;

public class RecordingPreparationUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task CancelDuringHandoffPreventsFormalStart()
    {
        var source = new StoppingSession();
        await using var coordinator = new ScreenRecorder.Recorder.Services.PreflightCoordinator(new(), () => source);
        await using var dispatcher = new ScreenRecorder.Recorder.Services.PreflightIpcDispatcher(coordinator);
        var window = new RecordingPreflightWindow(new(), (command, request, _) => dispatcher.DispatchAsync(new()
        {
            MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request)
        }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        window.Show();
        while (!((RecordingPreflightViewModel)window.DataContext!).CanStart) await Task.Delay(10, deadline.Token);
        window.FindControl<Button>("StartButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await source.Stopping.Task.WaitAsync(deadline.Token);
        window.FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        source.Release.TrySetResult();
        try
        {
            Assert.Null(await window.Confirmation.WaitAsync(deadline.Token));
            Assert.False(window.IsVisible);
        }
        finally { window.SetPreparationState(RecordingPreparationState.Started); }
    }

    private sealed class StoppingSession : ScreenRecorder.Recorder.Services.IPreflightLiveSession
    {
        public TaskCompletionSource Stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(ScreenRecorder.Core.Models.RecordingConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public ScreenRecorder.Core.Preflight.PreflightStatus ReadStatus(DateTimeOffset now) =>
            ScreenRecorder.Core.Preflight.PreflightStatus.Empty with
            { Phase = ScreenRecorder.Core.Preflight.PreflightPhase.Checking, AudioReady = true };
        public ScreenRecorder.Infrastructure.IPC.ProjectFrameReply? ReadFrame() => null;
        public async Task StopAsync(CancellationToken ct) { Stopping.TrySetResult(); await Release.Task.WaitAsync(ct); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DelayedStartKeepsWindowOpenAndUnconfirmedStartCannotCloseOrRestart()
    {
        var window = new RecordingPreflightWindow(new(), (_, _, _) =>
            throw new InvalidOperationException("Preparation-only window must not start preflight."), preparationOnly: true);
        window.Show();
        Assert.True(window.IsVisible);
        Assert.False(window.FindControl<Grid>("AudioCards")!.IsVisible);
        Assert.Equal(LanguageManager.Instance["RecordingPreparing"],
            window.FindControl<TextBlock>("StatusMessage")!.Text);
        window.Close();
        Assert.True(window.IsVisible);
        window.SetPreparationState(RecordingPreparationState.Unconfirmed);
        window.Close();
        Assert.True(window.IsVisible);
        Assert.False(window.FindControl<Button>("StartButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("CancelButton")!.IsEnabled);
        Assert.Equal(LanguageManager.Instance["RecordingConfirming"],
            window.FindControl<TextBlock>("StatusMessage")!.Text);
        window.SetPreparationState(RecordingPreparationState.Started);
        Assert.False(window.IsVisible);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void KnownFailureKeepsReadableStatusAndAllowsDismissal()
    {
        var window = new RecordingPreflightWindow(new(), (_, _, _) =>
            throw new InvalidOperationException(), preparationOnly: true);
        window.Show();
        window.SetPreparationState(RecordingPreparationState.Failed);
        Assert.True(window.IsVisible);
        Assert.False(window.FindControl<ProgressBar>("StatusActivity")!.IsVisible);
        Assert.True(window.FindControl<Button>("CancelButton")!.IsEnabled);
        window.Close();
        Assert.False(window.IsVisible);
    }
}
