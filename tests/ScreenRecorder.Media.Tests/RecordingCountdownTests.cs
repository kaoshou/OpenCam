using ScreenRecorder.UI.Recording;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingCountdownTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task CancelingActualCountdownDialogReturnsFalse()
    {
        var owner = new Avalonia.Controls.Window(); owner.Show();
        var window = new RecordingCountdownWindow(3);
        var result = window.ShowDialog<bool>(owner);
        window.CancelCountdown();
        Assert.False(await result); window.CancelCountdown(); owner.Close();
    }
    [Fact]
    public async Task CancelNeverCompletesCountdown()
    {
        using var cancel = new CancellationTokenSource();
        var shown = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecordingCountdown.RunAsync(3,
            n => { shown.Add(n); cancel.Cancel(); }, cancel.Token));
        Assert.Equal(new[] { 3 }, shown);
    }
    [Fact]
    public async Task OffStartsImmediatelyButAlreadyCanceledCannotStart()
    {
        await RecordingCountdown.RunAsync(0, _ => throw new Exception("Off must not show a countdown"), default);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecordingCountdown.RunAsync(0, _ => { }, cancel.Token));
    }
    [Fact]
    public async Task SlowUiUsesElapsedTimeInsteadOfAccumulatingTicks()
    {
        var time = new JumpClock(); var values = new List<int>();
        await RecordingCountdown.RunAsync(3, n => { values.Add(n); time.Now += 4 * TimeSpan.TicksPerSecond; }, default, time);
        Assert.Equal(new[] { 3 }, values);
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
        vm.SelectedCountdownSeconds = 5;
        await vm.SaveAsync();
        Assert.Equal(1, store.SaveCount); Assert.Equal(5, store.CurrentSettings.RecordingCountdownSeconds);
        Assert.Equal("M", store.CurrentSettings.BookmarkHotkeyKey); Assert.Equal(6u, store.CurrentSettings.BookmarkHotkeyModifiers);
    }
    private sealed class JumpClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
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
        public Task<ScreenRecorder.Core.Models.UserSettings> LoadSettingsAsync(CancellationToken ct = default) => Task.FromResult(new ScreenRecorder.Core.Models.UserSettings());
        public Task SaveSettingsAsync(ScreenRecorder.Core.Models.UserSettings settings, CancellationToken ct = default) => throw new IOException("disk full");
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountdownFollowsPreflightAndCancelCannotCreateCapture(bool accept)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ScreenRecorder.Recorder.Services.ProjectRecordingCoordinator(scope.Recorder,
            new ScreenRecorder.Infrastructure.Projects.JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ScreenRecorder.Recorder.Services.ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        main.ConfigureRecordingContent(new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = System.Text.Json.JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Countdown");
        var checkedSources = false; var countdowns = 0;
        main.RequestRecordingPreflight = (configuration, _) => { checkedSources = true; return Task.FromResult<ScreenRecorder.Core.Models.RecordingConfiguration?>(configuration); };
        main.RequestRecordingCountdown = seconds =>
        {
            Assert.Equal(3, seconds); Assert.True(checkedSources); Assert.Empty(scope.Factory.Paths);
            Assert.Null(coordinator.Current); countdowns++; return Task.FromResult(accept);
        };
        try
        {
            await main.StartRecordingAsync(); Assert.Equal(1, countdowns);
            if (accept)
            {
                Assert.True(main.IsRecording); await main.TogglePauseResumeAsync(); await main.TogglePauseResumeAsync();
                Assert.Equal(1, countdowns); Assert.Equal(2, scope.Factory.Paths.Count);
                Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
            }
            else { Assert.Empty(scope.Factory.Paths); Assert.Null(coordinator.Current); Assert.Null(scope.Recorder.CurrentSession); }
        }
        finally { main.Cleanup(); }
    }
}
