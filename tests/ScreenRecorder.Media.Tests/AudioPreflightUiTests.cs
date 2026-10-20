using Avalonia.Controls;
using Avalonia;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Preflight;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;

public class AudioPreflightUiTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void AudioReadyDoesNotRequireFrame(bool audio, bool frame, bool expected)
    {
        var operation = Guid.NewGuid();
        var model = new RecordingPreflightViewModel(operation);
        model.Apply(new(Guid.NewGuid(), PreflightStatus.Empty with
        {
            OperationId = operation, Phase = PreflightPhase.Checking,
            AudioReady = audio, HasFrame = frame,
            Microphone = new(PreflightSourceState.Silent)
        }));
        Assert.Equal(expected, model.CanStart);
        Assert.Equal(expected, model.CanTrial);
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public async Task NoFrameRequestsAndStatusAreaShowsEveryPhaseInBothLanguages(AppLanguage language)
    {
        var strings = LanguageManager.Instance;
        var previous = strings.CurrentLanguage;
        strings.CurrentLanguage = language;
        var status = PreflightStatus.Empty;
        var server = Guid.NewGuid();
        long sequence = 0;
        int frames = 0;
        var window = new RecordingPreflightWindow(new() { AudioSource = AudioSourceType.SystemAndMicrophone },
            (command, request, _) =>
            {
                if (command == "EndPreflight") status = status with { Phase = PreflightPhase.Closed };
                return Task.FromResult(new IpcResponse { Success = true, DataJson = JsonSerializer.Serialize(
                    new PreflightReply(server, status with { OperationId = request.OperationId, ConfigurationRevision = 1 },
                        Sequence: ++sequence)) });
            },
            (_, _) => { frames++; throw new InvalidOperationException("Audio checks must not request video."); });
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            window.Show();
            Assert.Null(window.FindControl<Image>("Preview"));
            var message = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("StatusMessage"));
            foreach (var (phase, key) in new[]
            {
                (PreflightPhase.Preparing, "PreflightPreparing"), (PreflightPhase.Checking, "PreflightChecking"),
                (PreflightPhase.TrialStarting, "PreflightTrialStarting"),
                (PreflightPhase.TrialRecording, "PreflightTrialRecording"),
                (PreflightPhase.TrialFinalizing, "PreflightTrialFinalizing"),
                (PreflightPhase.Reviewing, "PreflightTrialReady"), (PreflightPhase.Failed, "PreflightFailed")
            })
            {
                status = status with { Phase = phase, AudioReady = phase == PreflightPhase.Checking,
                    HasFrame = true, HasTrial = phase == PreflightPhase.Reviewing };
                var expected = phase == PreflightPhase.TrialRecording ? string.Format(strings[key], 5) : strings[key];
                while (message.Text != expected) await Task.Delay(10, deadline.Token);
                Assert.Equal(phase == PreflightPhase.TrialRecording,
                    window.FindControl<ProgressBar>("TrialProgress")!.IsVisible);
            }
            Assert.Equal(0, frames);
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            message.Text = string.Join(" ", Enumerable.Repeat(strings["PreflightFailed"], 12));
            window.UpdateLayout();
            foreach (var name in new[] { "StartButton", "CancelButton" })
            {
                var button = window.FindControl<Button>(name)!;
                Assert.True(button.IsVisible);
                Assert.True(button.Bounds.Width > 0);
                Assert.True(button.Bounds.Height > 0);
                var point = button.TranslatePoint(default, window);
                Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, window.ClientSize.Width - button.Bounds.Width);
                Assert.InRange(point.Value.Y, 0, window.ClientSize.Height - button.Bounds.Height);
            }
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            strings.CurrentLanguage = previous;
        }
    }
}
