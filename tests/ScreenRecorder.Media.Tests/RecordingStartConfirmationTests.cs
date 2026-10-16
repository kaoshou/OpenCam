using System.Text.Json;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingStartConfirmationTests
{
    [Fact]
    public void OldCountdownSettingIsIgnoredWithoutLosingOtherPreferences()
    {
        var settings = JsonSerializer.Deserialize<UserSettings>("""{"RecordingCountdownSeconds":5,"BookmarkHotkeyKey":"K","MicrophoneSilenceReminderEnabled":false}""")!;
        Assert.Equal("K", settings.BookmarkHotkeyKey);
        Assert.False(settings.MicrophoneSilenceReminderEnabled);
        Assert.DoesNotContain("RecordingCountdownSeconds", JsonSerializer.Serialize(settings));
    }

    [Fact]
    public async Task BookmarkShortcutConflictDoesNotSaveAndValidOptionsRoundtrip()
    {
        var store = new MockSettingsService(); var vm = new SettingsViewModel(store);
        await vm.LoadSettingsAsync();
        vm.SelectedBookmarkModifier = vm.SelectedStartStopModifier;
        vm.SelectedBookmarkKey = vm.SelectedStartStopKey;
        await vm.SaveAsync(); Assert.Equal(0, store.SaveCount); Assert.NotEmpty(vm.ErrorMessage!);
        vm.SelectedBookmarkModifier = vm.AvailableModifiers.Single(m => m.Value == 6);
        vm.SelectedBookmarkKey = vm.AvailableKeys.Single(k => k.KeyName == "M");
        await vm.SaveAsync();
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("M", store.CurrentSettings.BookmarkHotkeyKey); Assert.Equal(6u, store.CurrentSettings.BookmarkHotkeyModifiers);
    }

    [Fact]
    public async Task SettingsWriteFailureRestoresOriginalShortcut()
    {
        var vm = new SettingsViewModel(new FailedSettings()); await vm.LoadSettingsAsync();
        string? registered = null;
        vm.TryApplyHotkeys = s => { registered = s.BookmarkHotkeyKey; return null; };
        vm.SelectedBookmarkKey = vm.AvailableKeys.Single(k => k.KeyName == "S");
        await vm.SaveAsync();
        Assert.Equal("M", registered); Assert.Contains("disk full", vm.ErrorMessage);
    }
    private sealed class FailedSettings : ScreenRecorder.Core.Interfaces.ISettingsService
    {
        public Task<UserSettings> LoadSettingsAsync(CancellationToken ct = default) => Task.FromResult(new UserSettings());
        public Task SaveSettingsAsync(UserSettings settings, CancellationToken ct = default) => throw new IOException("disk full");
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreflightIsTheOnlyConfirmationBeforeRecording(bool accept)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            new ScreenRecorder.Infrastructure.Projects.JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        main.ConfigureRecordingContent(new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Confirmed recording");
        var confirmations = 0;
        main.RequestRecordingPreflight = (configuration, _) =>
        {
            Assert.Empty(scope.Factory.Paths); Assert.Null(coordinator.Current); confirmations++;
            return Task.FromResult<RecordingConfiguration?>(accept ? configuration : null);
        };
        try
        {
            await main.StartRecordingAsync();
            Assert.Equal(1, confirmations);
            if (accept)
            {
                Assert.True(main.IsRecording); Assert.Single(scope.Factory.Paths);
                Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
            }
            else { Assert.Empty(scope.Factory.Paths); Assert.Null(coordinator.Current); Assert.Null(scope.Recorder.CurrentSession); }
        }
        finally { main.Cleanup(); }
    }
}
