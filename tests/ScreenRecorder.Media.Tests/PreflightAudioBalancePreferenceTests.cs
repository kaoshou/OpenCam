using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyConfirmedAudioBalanceBecomesNextRecordingDefault(bool confirm)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            new ScreenRecorder.Infrastructure.Projects.JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        main.ApplyNewSettings(new UserSettings { SystemAudioVolumePercent = 90, MicrophoneVolumePercent = 80 });
        main.RecordMicrophone = true;
        main.RecordSystemAudio = false;
        main.ConfigureRecordingContent(new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Audio balance");
        main.RequestRecordingPreflight = (configuration, _) =>
        {
            var edited = System.Text.Json.JsonSerializer.Deserialize<RecordingConfiguration>(System.Text.Json.JsonSerializer.Serialize(configuration))!;
            edited.SystemAudioVolumePercent = 25; edited.MicrophoneVolumePercent = 60;
            return Task.FromResult<RecordingConfiguration?>(confirm ? edited : null);
        };
        try
        {
            await main.StartRecordingAsync();
            await main.FlushPendingUserSettingsAsync();
            var saved = await main.SettingsService.LoadSettingsAsync();
            Assert.Equal(confirm ? 25 : 90, saved.SystemAudioVolumePercent);
            Assert.Equal(confirm ? 60 : 80, saved.MicrophoneVolumePercent);
            Assert.Equal(confirm, main.IsRecording);
            if (confirm)
            {
                Assert.Equal(60, scope.Recorder.CurrentSession!.Configuration.MicrophoneVolumePercent);
                await main.TogglePauseResumeAsync();
                await main.TogglePauseResumeAsync();
                Assert.Equal(60, scope.Recorder.CurrentSession.Configuration.MicrophoneVolumePercent);
                Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
            }
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task RecordingConfigurationKeepsSavedAudioBalanceAcrossPreferenceSaves()
    {
        var main = CreatePreflightApprovedMain();
        try
        {
            main.ApplyNewSettings(new UserSettings { SystemAudioVolumePercent = 25, MicrophoneVolumePercent = 80 });
            main.SelectedFps = 20;
            await main.FlushPendingUserSettingsAsync();
            var saved = await main.SettingsService.LoadSettingsAsync();
            Assert.Equal(25, saved.SystemAudioVolumePercent);
            Assert.Equal(80, saved.MicrophoneVolumePercent);
            var configuration = main.BuildProjectConfiguration();
            Assert.Equal(25, configuration.SystemAudioVolumePercent);
            Assert.Equal(80, configuration.MicrophoneVolumePercent);
        }
        finally { main.Cleanup(); }
    }
}
