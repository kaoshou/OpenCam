// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.ViewModels;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class FoolproofUiLogicTests
{
    [Fact]
    public void DocumentationScreenshot_UsesNeutralTemporaryProfile()
    {
        var vm = new MainViewModel(forScreenshot: true);

        Assert.True(vm.IsMonitorSelected);
        Assert.False(vm.IsCustomRegion);
        Assert.False(vm.RecordMicrophone);
        Assert.False(vm.RecordSystemAudio);
        Assert.Equal(0, vm.RecoverableSessionCount);
        Assert.StartsWith(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            vm.OutputDirectory, StringComparison.Ordinal);
        Assert.False(vm.ShouldSendGlobalShutdownOnCleanup);
        vm.Cleanup();
    }

    [Fact]
    public void DocumentationScreenshot_FindsFlagRegardlessOfArgumentOrderOrCase()
    {
        Assert.Equal(2, ScreenRecorder.UI.App.ScreenshotArgumentIndex(
            ["--lang", "en", "--SCREENSHOT", "/private/tmp/preview.png"]));
        Assert.Equal(0, ScreenRecorder.UI.App.ScreenshotArgumentIndex(
            ["--screenshot", "/private/tmp/preview.png"]));
        Assert.Equal(-1, ScreenRecorder.UI.App.ScreenshotArgumentIndex(["--lang", "zh"]));
    }

    [Fact]
    public void DocumentationRecordingScreenshot_ShowsActiveAudioWaveforms()
    {
        var vm = new MainViewModel(forScreenshot: true);

        vm.SetDocumentationRecordingAudioActivity();

        Assert.True(vm.RecordSystemAudio);
        Assert.True(vm.RecordMicrophone);
        Assert.Contains(vm.SystemWaveformBars, bar => bar.Height > 0);
        Assert.Contains(vm.MicrophoneWaveformBars, bar => bar.Height > 0);
        Assert.Equal("收音中", vm.SystemAudioMeterStateText);
        Assert.Equal("收音中", vm.MicrophoneMeterStateText);
        vm.Cleanup();
    }

    [Fact]
    public void UnconfirmedStartup_AllowsStopButBlocksCloseAndSettings()
    {
        var vm = new MainViewModel { IsPreparing = true };

        vm.EnterUnconfirmedStartup();

        Assert.False(vm.IsPreparing);
        Assert.True(vm.IsRecording);
        Assert.True(vm.CanStopRecording);
        Assert.False(vm.CanPauseOrResume);
        Assert.False(vm.CanEditRecordingSettings);
        Assert.True(vm.IsApplicationCloseBlocked);
    }

    [Fact]
    public void ForceQuitRequiresUnconfirmedStartupAndFailedNormalStop()
    {
        var vm = new MainViewModel { IsPreparing = true };
        vm.EnterUnconfirmedStartup();

        Assert.False(vm.CanForceQuitUnconfirmed);
        Assert.False(vm.TryAuthorizeForceQuit());
        Assert.True(vm.IsApplicationCloseBlocked);

        vm.MarkUnconfirmedStopFailure();

        Assert.True(vm.CanForceQuitUnconfirmed);
        Assert.True(vm.IsApplicationCloseBlocked);
        Assert.True(vm.TryAuthorizeForceQuit());
        Assert.False(vm.IsApplicationCloseBlocked);
    }

    [Fact]
    public void ConfirmedRecordingCannotAuthorizeForceQuit()
    {
        var vm = new MainViewModel { IsRecording = true };

        Assert.False(vm.CanForceQuitUnconfirmed);
        Assert.False(vm.TryAuthorizeForceQuit());
        Assert.True(vm.IsApplicationCloseBlocked);
    }

    [Fact]
    public void ApplyRuntimeSettings_PropagatesPersistedRecordingOptions()
    {
        var config = MainViewModel.ApplyRuntimeSettings(
            new ScreenRecorder.Core.Models.RecordingConfiguration(),
            videoQualityPreset: "Ultra",
            deleteWorkingFileAfterRemux: true,
            diskWarningThresholdGb: 5.0,
            diskCriticalThresholdMb: 1000.0);

        Assert.Equal("Ultra", config.VideoQualityPreset);
        Assert.True(config.DeleteWorkingFileAfterSuccessfulRemux);
        Assert.Equal(5L * 1024 * 1024 * 1024, config.DiskWarningThresholdBytes);
        Assert.Equal(1000L * 1024 * 1024, config.DiskCriticalThresholdBytes);
    }

    [Theory]
    [InlineData(RecordingState.Recording, 4.9, 5.0, true)]
    [InlineData(RecordingState.Paused, 5.0, 5.0, true)]
    [InlineData(RecordingState.Recording, 5.1, 5.0, false)]
    [InlineData(RecordingState.Stopping, 0.0, 5.0, false)]
    [InlineData(RecordingState.Finalizing, 0.0, 5.0, false)]
    [InlineData(RecordingState.Completed, 0.0, 5.0, false)]
    [InlineData(RecordingState.Failed, 0.0, 5.0, false)]
    public void DiskWarning_OnlyAppliesToActiveRecordingStates(
        RecordingState state,
        double availableGb,
        double warningThresholdGb,
        bool expected)
    {
        var availableBytes = (long)(availableGb * 1024 * 1024 * 1024);
        var warningBytes = (long)(warningThresholdGb * 1024 * 1024 * 1024);

        Assert.Equal(
            expected,
            MainViewModel.IsDiskSpaceWarning(state, availableBytes, warningBytes));
    }

    [Fact]
    public void ApplyRuntimeSettings_InvalidThresholdPairUsesSafeDefaults()
    {
        var config = MainViewModel.ApplyRuntimeSettings(
            new ScreenRecorder.Core.Models.RecordingConfiguration(),
            videoQualityPreset: "Standard",
            deleteWorkingFileAfterRemux: false,
            diskWarningThresholdGb: 0.25,
            diskCriticalThresholdMb: 500.0);

        Assert.Equal(
            ScreenRecorder.Core.Models.RecordingConfiguration.DefaultDiskWarningThresholdBytes,
            config.DiskWarningThresholdBytes);
        Assert.Equal(
            ScreenRecorder.Core.Models.RecordingConfiguration.DefaultDiskCriticalThresholdBytes,
            config.DiskCriticalThresholdBytes);
    }

    [Fact]
    public void SupportsSystemAudio_RequiresSupportedPlatformAndBundledHelper()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCamUiSupport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.True(MainViewModel.SupportsSystemAudioOnPlatform(
                isWindows: true,
                isMacOS: false,
                new Version(10, 0),
                directory));
            Assert.False(MainViewModel.SupportsSystemAudioOnPlatform(
                isWindows: false,
                isMacOS: true,
                new Version(12, 6),
                directory));
            Assert.False(MainViewModel.SupportsSystemAudioOnPlatform(
                isWindows: false,
                isMacOS: true,
                new Version(13, 0),
                directory));

            File.WriteAllText(
                Path.Combine(directory, "OpenCam.SystemAudio"),
                "fixture");

            Assert.True(MainViewModel.SupportsSystemAudioOnPlatform(
                isWindows: false,
                isMacOS: true,
                new Version(13, 0),
                directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, true, false, AudioSourceType.None)]
    [InlineData(false, true, true, AudioSourceType.MicrophoneOnly)]
    [InlineData(true, true, false, AudioSourceType.SystemOnly)]
    [InlineData(true, true, true, AudioSourceType.SystemAndMicrophone)]
    public void ResolveAudioSource_RespectsCapabilities(
        bool supportsSystemAudio,
        bool recordSystemAudio,
        bool recordMicrophone,
        AudioSourceType expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.ResolveAudioSource(
                supportsSystemAudio,
                recordSystemAudio,
                recordMicrophone));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public void FixedRecordingSettings_AreEditableOnlyWhileFullyIdle(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.CanEditRecordingSettingsForState(
                isRecording,
                isPaused,
                isPreparing));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void PausedAdjustableSettings_AreEditableWhenIdleOrPaused(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.CanEditPausedSettingsForState(
                isRecording,
                isPaused,
                isPreparing));
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void Recovery_IsAvailableOnlyWhileFullyIdle(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool isRecovering,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.CanRecoverSessionsForState(
                isRecording,
                isPaused,
                isPreparing,
                isRecovering));
    }

    [Fact]
    public void Recovery_LocksStartingAndAllRecordingSettings()
    {
        Assert.False(MainViewModel.CanStartRecordingForState(
            isRecording: false,
            isPaused: false,
            isPreparing: false,
            isRecovering: true));
        Assert.False(MainViewModel.CanEditRecordingSettingsForState(
            isRecording: false,
            isPaused: false,
            isPreparing: false,
            isRecovering: true));
        Assert.False(MainViewModel.CanEditPausedSettingsForState(
            isRecording: false,
            isPaused: false,
            isPreparing: false,
            isRecovering: true));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void Closing_IsBlockedWhileRecordingSessionIsActive(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.ShouldBlockApplicationClose(
                isRecording,
                isPaused,
                isPreparing));
    }

    [Theory]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, true, false, true)]
    public void StopResponse_ClearsActiveStateOnlyAfterConfirmedSuccess(
        bool stopSucceeded,
        bool wasRecording,
        bool wasPaused,
        bool expectedRecording,
        bool expectedPaused)
    {
        var result = MainViewModel.ResolveStateAfterStopResponse(
            stopSucceeded,
            wasRecording,
            wasPaused);

        Assert.Equal(expectedRecording, result.IsRecording);
        Assert.Equal(expectedPaused, result.IsPaused);
    }

    [Theory]
    [InlineData(0, 0, 0, "NoRecoverable")]
    [InlineData(2, 0, 0, "Success")]
    [InlineData(1, 1, 0, "PartialSuccess")]
    [InlineData(0, 1, 1, "PartialSuccess")]
    [InlineData(0, 0, 2, "Failed")]
    public void RecoveryOutcome_DistinguishesSuccessAndFailure(
        int recoveredCount,
        int partialCount,
        int failedCount,
        string expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.ResolveRecoveryOutcome(
                recoveredCount,
                partialCount,
                failedCount).ToString());
    }

    [Fact]
    public void CustomRegion_CanConfigureOnlyWhenSelectedAndNotRecording()
    {
        var vm = new MainViewModel(forScreenshot: true);

        // Start from an explicit unselected state. The documented profile
        // intentionally defaults to the primary monitor.
        vm.IsMonitorSelected = false;

        // 預設為全螢幕，調整選區按鈕應禁用 (防呆)

        Assert.False(vm.IsCustomRegion);
        Assert.False(vm.CanConfigureCustomRegion, "全螢幕模式下禁止點擊調整自訂區域按鈕");

        // 使用者點選「自訂區域」
        vm.IsCustomRegion = true;

        Assert.True(vm.CanConfigureCustomRegion, "選取自訂區域時應啟用調整按鈕");

        // 錄影中應全面鎖定
        vm.IsRecording = true;
        Assert.False(vm.CanConfigureCustomRegion, "錄影進行中應全面鎖定選區調整按鈕");
    }

    [Fact]
    public void MonitorSelection_CanSelectOnlyWhenOptionCheckedAndNotRecording()
    {
        var vm = new MainViewModel(forScreenshot: true);

        vm.IsMonitorSelected = false;

        // 預設為全螢幕，指定螢幕選單應禁用
        Assert.False(vm.IsMonitorSelected);
        Assert.False(vm.CanSelectMonitor);

        // 切換到指定螢幕
        vm.IsMonitorSelected = true;

        Assert.True(vm.CanSelectMonitor);

        // 錄影中鎖定
        vm.IsRecording = true;
        Assert.False(vm.CanSelectMonitor);
    }

    [Fact]
    public void MicrophoneSelection_CanSelectOnlyWhenEnabledAndNotRecording()
    {
        var vm = new MainViewModel(forScreenshot: true);

        // 預設不錄麥克風
        vm.RecordMicrophone = false;
        Assert.False(vm.CanSelectMicrophone);

        // 勾選錄製麥克風
        vm.RecordMicrophone = true;
        Assert.True(vm.CanSelectMicrophone);

        // 錄影中鎖定
        vm.IsRecording = true;
        Assert.False(vm.CanSelectMicrophone);
    }

    [Fact]
    public void UpdateCustomRegion_ShouldSanitizeDimensionsToEvenAndAutoSelectRegion()
    {
        var vm = new MainViewModel(forScreenshot: true);


        // 傳入可放入 CI 虛擬顯示器的奇數解析度，避免測試被正確的螢幕邊界裁切干擾。
        vm.UpdateCustomRegion(50, 60, 639, 479);

        // 驗收：自動轉為偶數 (638 x 478)，且狀態切換為自訂區域
        Assert.True(vm.RegionWidth % 2 == 0);
        Assert.True(vm.RegionHeight % 2 == 0);
        Assert.Equal(638, vm.RegionWidth);
        Assert.Equal(478, vm.RegionHeight);
        Assert.True(vm.IsCustomRegion, "更新選區後必須自動將錄影範圍設置為自訂區域");

    }

    [Fact]
    public void CursorEffect_ShouldInitializeWithAllOptionsAndLocalize()
    {
        var vm = new MainViewModel(forScreenshot: true);

        // 驗收：4 種模式皆存在且預設為 Default
        Assert.Equal(4, vm.AvailableCursorEffects.Count);
        Assert.NotNull(vm.SelectedCursorEffect);
        Assert.Equal(ScreenRecorder.Core.Enums.CursorEffectMode.Default, vm.SelectedCursorEffect.Mode);

        // 切換到英文
        vm.Strings.CurrentLanguage = ScreenRecorder.Core.Localization.AppLanguage.EnUs;
        var enHidden = vm.AvailableCursorEffects.First(e => e.Mode == ScreenRecorder.Core.Enums.CursorEffectMode.Hidden);
        Assert.Equal("Hide Cursor (Clean View)", enHidden.DisplayName);

        // 切換回繁體中文
        vm.Strings.CurrentLanguage = ScreenRecorder.Core.Localization.AppLanguage.ZhTw;
        var zhHidden = vm.AvailableCursorEffects.First(e => e.Mode == ScreenRecorder.Core.Enums.CursorEffectMode.Hidden);
        Assert.Equal("隱藏滑鼠游標 (不錄游標)", zhHidden.DisplayName);
    }

    [Fact]
    public async Task QueryTelemetry_OnConsecutiveFailures_ShouldStopAndPromptRecovery()
    {
        var vm = new MainViewModel(forScreenshot: true);
        vm.IsRecording = true;

        // 連續失敗門檻以次數為準；IPC timeout 與排程可能使實際時間不同。
        for (var attempt = 1; attempt < 6; attempt++)
        {
            await vm.QueryTelemetryAsync();
            Assert.True(vm.IsRecording, $"第 {attempt} 次失敗不得貿然判斷中斷");
        }

        await vm.QueryTelemetryAsync();
        Assert.False(vm.IsRecording, "連續 6 次連線失敗時應自動安全收斂停止狀態");
        Assert.Contains("核心無預警中斷", vm.StatusMessage);
    }

    [Fact]
    public async Task QueryTelemetry_WhenRecorderFails_ShowsEngineFailureBeforeStoppingPolling()
    {
        var pipeName = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var telemetry = new RecorderTelemetry
        {
            State = RecordingState.Failed,
            IsEncoderHealthy = false,
            HealthWarning = "FFmpeg exited unexpectedly."
        };
        await using var server = new NamedPipeIpcServer(
            pipeName,
            _ => Task.FromResult(new IpcResponse
            {
                Success = true,
                ErrorMessage = JsonSerializer.Serialize(telemetry)
            }));
        server.Start();
        await Task.Delay(100);
        await using var client = new NamedPipeIpcClient(pipeName);
        var readiness = await client.SendCommandAsync("GetTelemetry", new { }, timeoutMs: 1500);
        Assert.True(readiness.Success, readiness.ErrorMessage);
        var vm = new MainViewModel(forScreenshot: true)
        {
            IsRecording = true,
            StatusMessage = "recording"
        };
        vm.SetIpcClientForTesting(client);

        await vm.QueryTelemetryAsync();

        Assert.False(vm.IsRecording);
        Assert.Contains("FFmpeg exited unexpectedly.", vm.StatusMessage);
        vm.Cleanup();
    }

    [Fact]
    public void ResolveRecorderHealthStatus_UsesWarningWithoutOverwritingDiskWarning()
    {
        var telemetry = new RecorderTelemetry
        {
            State = RecordingState.Recording,
            IsVideoCaptureHealthy = false,
            IsEncoderHealthy = false,
            HealthWarning = "Recording output has stopped growing."
        };
        var diskWarning = LanguageManager.Instance.GetFormatted(
            "StatusDiskSpaceWarning", 1.5);

        Assert.Equal(
            diskWarning,
            MainViewModel.ResolveRecorderHealthStatus(telemetry, diskWarning));
        Assert.Contains(
            telemetry.HealthWarning,
            MainViewModel.ResolveRecorderHealthStatus(telemetry, "recording"));
    }

    [Fact]
    public void ResolveRecorderHealthStatus_LeavesHealthyRecordingStatusUnchanged()
    {
        var telemetry = new RecorderTelemetry
        {
            State = RecordingState.Recording,
            IsVideoCaptureHealthy = true,
            IsEncoderHealthy = true
        };

        Assert.Equal(
            "recording",
            MainViewModel.ResolveRecorderHealthStatus(telemetry, "recording"));
    }
}
