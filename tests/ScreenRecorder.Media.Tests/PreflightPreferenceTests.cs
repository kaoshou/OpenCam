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
    public async Task SettingsDropdownLoadsSavesAndResetsPreflightMode(AppLanguage language)
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
            var dropdown = window.FindControl<ComboBox>("RecordingPreflightModePicker");
            Assert.NotNull(dropdown);
            Assert.Equal(3, dropdown.ItemCount);
            Assert.Equal(0, dropdown.SelectedIndex);
            Assert.Equal(language == AppLanguage.ZhTw ? "錄製前確認聲音" : "Check audio before recording",
                window.FindControl<TextBlock>("RecordingPreflightLabel")!.Text);
            dropdown.SelectedIndex = 2;
            await vm.SaveAsync();
            Assert.Equal(2, JsonSerializer.SerializeToElement(store.CurrentSettings).GetProperty("RecordingPreflightMode").GetInt32());
            strings.CurrentLanguage = language == AppLanguage.ZhTw ? AppLanguage.EnUs : AppLanguage.ZhTw;
            Assert.Equal(2, dropdown.SelectedIndex);
            strings.CurrentLanguage = language;
            Assert.Equal(2, dropdown.SelectedIndex);
            dropdown.SelectedIndex = 0;
            await vm.SaveAsync();
            Assert.Equal(0, JsonSerializer.SerializeToElement(store.CurrentSettings).GetProperty("RecordingPreflightMode").GetInt32());
            vm.ResetToDefaults();
            Assert.Equal(1, dropdown.SelectedIndex);
            await vm.SaveAsync();
            Assert.Equal(1, JsonSerializer.SerializeToElement(store.CurrentSettings).GetProperty("RecordingPreflightMode").GetInt32());
        }
        finally { window.Close(); strings.CurrentLanguage = originalLanguage; }
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(0, true, false, 0, 0, 0)]
    [InlineData(1, true, false, 1, 1, 1)]
    [InlineData(2, true, false, 1, 2, 3)]
    [InlineData(0, true, true, 0, 0, 0)]
    [InlineData(1, true, true, 1, 2, 2)]
    [InlineData(2, true, true, 1, 2, 3)]
    [InlineData(1, false, false, 0, 0, 0)]
    [InlineData(2, false, false, 0, 0, 0)]
    public async Task PreflightModeAppliesToNewRecordingResumeAndInsertAndAlwaysShowsPreparation(
        int mode, bool selected, bool insert, int startChecks, int totalChecks, int repeatedResumeChecks)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var main = CreatePreflightApprovedMain();
        main.ConfigureRecordingContent(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), () => true, () => Task.CompletedTask);
        main.OutputDirectory = scope.Configuration.OutputDirectory;
        main.RequestRecordingProjectName = _ => Task.FromResult<string?>("Preflight preference");
        main.ApplyNewSettings(JsonSerializer.Deserialize<UserSettings>($"{{\"RecordingPreflightMode\":{mode}}}")!);
        main.RecordMicrophone = selected;
        main.RecordSystemAudio = false;
        var preparation = new List<ScreenRecorder.UI.Preflight.RecordingPreparationState>();
        main.RecordingPreparationChanged = preparation.Add;
        var checks = 0;
        main.RequestRecordingPreflight = (configuration, _) =>
        {
            checks++;
            return Task.FromResult<RecordingConfiguration?>(configuration);
        };
        try
        {
            await main.StartRecordingAsync();
            Assert.Equal(startChecks, checks);
            Assert.Contains(ScreenRecorder.UI.Preflight.RecordingPreparationState.Preparing, preparation);
            Assert.Equal(ScreenRecorder.UI.Preflight.RecordingPreparationState.Started, preparation.Last());
            Assert.True(main.IsRecording);
            Assert.Equal(ProjectMode.Recording, coordinator.Mode);
            await main.TogglePauseResumeAsync();
            if (insert) await main.StartEditorRecordingAsync(0, coordinator.Current!.Revision);
            else await main.TogglePauseResumeAsync();
            Assert.Equal(totalChecks, checks);
            Assert.Equal(2, preparation.Count(s => s == ScreenRecorder.UI.Preflight.RecordingPreparationState.Started));
            Assert.True(main.IsRecording);
            Assert.Equal(2, scope.Factory.Paths.Count);
            await main.TogglePauseResumeAsync();
            await main.TogglePauseResumeAsync();
            Assert.Equal(repeatedResumeChecks, checks);
            Assert.True(main.IsRecording);
            Assert.Equal(3, scope.Factory.Paths.Count);
            Assert.Equal(3, preparation.Count(s => s == ScreenRecorder.UI.Preflight.RecordingPreparationState.Started));
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
            main.ApplyNewSettings(JsonSerializer.Deserialize<UserSettings>("{\"RecordingPreflightMode\":2}")!);
            main.RecordMicrophone = true;
            main.RequestRecordingPreflight = (_, _) => Task.FromResult<RecordingConfiguration?>(null);
            await main.TogglePauseResumeAsync();
            Assert.Equal(ProjectMode.Paused, coordinator.Mode);
            Assert.Single(scope.Factory.Paths);
            Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        }
        finally { main.Cleanup(); }
    }
}
