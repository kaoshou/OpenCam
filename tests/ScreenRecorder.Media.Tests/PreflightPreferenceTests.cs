// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia.Controls;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class PreflightPreferenceUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public async Task SettingsToggleLoadsSavesAndResetsPreflightPreference(AppLanguage language)
    {
        var strings = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var originalLanguage = strings.CurrentLanguage;
        var store = new MockSettingsService
        {
            CurrentSettings = JsonSerializer.Deserialize<UserSettings>("{\"RecordingPreflightEnabled\":false}")!
        };
        var vm = new SettingsViewModel(store);
        await vm.LoadSettingsAsync();
        strings.CurrentLanguage = language;
        var window = new SettingsWindow { DataContext = vm };
        try
        {
            window.Show();
            var toggle = window.FindControl<CheckBox>("RecordingPreflightToggle");
            Assert.NotNull(toggle);
            Assert.False(toggle.IsChecked);
            var label = Assert.IsType<TextBlock>(toggle.Content).Text;
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual("RecordingPreflightSetting", label);
            Assert.Equal(language == AppLanguage.ZhTw ? "錄製前確認聲音（建議開啟）" : "Check audio before recording (recommended)", label);
            toggle.IsChecked = true;
            await vm.SaveAsync();
            Assert.True(JsonSerializer.SerializeToElement(store.CurrentSettings).GetProperty("RecordingPreflightEnabled").GetBoolean());
            toggle.IsChecked = false;
            await vm.SaveAsync();
            Assert.False(JsonSerializer.SerializeToElement(store.CurrentSettings).GetProperty("RecordingPreflightEnabled").GetBoolean());
            vm.ResetToDefaults();
            Assert.True(toggle.IsChecked);
        }
        finally { window.Close(); strings.CurrentLanguage = originalLanguage; }
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreflightPreferenceAppliesToNewRecordingAndResume(bool enabled)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Preflight preference");
        main.ApplyNewSettings(JsonSerializer.Deserialize<UserSettings>($"{{\"RecordingPreflightEnabled\":{(enabled ? "true" : "false")}}}")!);
        var checks = 0;
        main.RequestRecordingPreflight = (configuration, _) =>
        {
            checks++;
            return Task.FromResult<RecordingConfiguration?>(enabled ? configuration : null);
        };
        try
        {
            await main.StartRecordingAsync();
            Assert.Equal(enabled ? 1 : 0, checks);
            Assert.True(main.IsRecording);
            Assert.Equal(ProjectMode.Recording, coordinator.Mode);
            await main.TogglePauseResumeAsync();
            await main.TogglePauseResumeAsync();
            Assert.Equal(enabled ? 2 : 0, checks);
            Assert.True(main.IsRecording);
            Assert.Equal(2, scope.Factory.Paths.Count);
            Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        }
        finally { main.Cleanup(); }
    }

    [Fact]
    public async Task SkippingPreflightDoesNotBypassPermissionAndReenabledCheckCanCancelResume()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        var permission = false;
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => permission, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Permission stays required");
        main.ApplyNewSettings(JsonSerializer.Deserialize<UserSettings>("{\"RecordingPreflightEnabled\":false}")!);
        main.RequestRecordingPreflight = (_, _) => throw new InvalidOperationException("Disabled check must not open.");
        try
        {
            await main.StartRecordingAsync();
            Assert.Empty(scope.Factory.Paths);
            Assert.Null(coordinator.Current);
            permission = true;
            await main.StartRecordingAsync();
            Assert.True(main.IsRecording);
            await main.TogglePauseResumeAsync();
            main.ApplyNewSettings(new UserSettings()); // Default-on settings take effect immediately.
            main.RequestRecordingPreflight = (_, _) => Task.FromResult<RecordingConfiguration?>(null);
            await main.TogglePauseResumeAsync();
            Assert.Equal(ProjectMode.Paused, coordinator.Mode);
            Assert.Single(scope.Factory.Paths);
            Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        }
        finally { main.Cleanup(); }
    }
}
