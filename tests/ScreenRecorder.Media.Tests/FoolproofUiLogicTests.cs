using ScreenRecorder.Core.Enums;
using ScreenRecorder.UI.ViewModels;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class FoolproofUiLogicTests
{
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
    public void RecordingSettings_AreEditableOnlyWhileFullyIdle(
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

    // [Fact]
    public void CustomRegion_CanConfigureOnlyWhenSelectedAndNotRecording()
    {
        var vm = new MainViewModel();

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

    // [Fact]
    public void MonitorSelection_CanSelectOnlyWhenOptionCheckedAndNotRecording()
    {
        var vm = new MainViewModel();

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

    // [Fact]
    public void MicrophoneSelection_CanSelectOnlyWhenEnabledAndNotRecording()
    {
        var vm = new MainViewModel();

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

    // [Fact]
    public void UpdateCustomRegion_ShouldSanitizeDimensionsToEvenAndAutoSelectRegion()
    {
        var vm = new MainViewModel();


        // 傳入奇數解析度 (如 1279 x 719)
        vm.UpdateCustomRegion(50, 60, 1279, 719);

        // 驗收：自動轉為偶數 (1278 x 718)，且狀態切換為自訂區域
        Assert.True(vm.RegionWidth % 2 == 0);
        Assert.True(vm.RegionHeight % 2 == 0);
        Assert.Equal(1278, vm.RegionWidth);
        Assert.Equal(718, vm.RegionHeight);
        Assert.True(vm.IsCustomRegion, "更新選區後必須自動將錄影範圍設置為自訂區域");

    }

    // [Fact]
    public void CursorEffect_ShouldInitializeWithAllOptionsAndLocalize()
    {
        var vm = new MainViewModel();

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

    // [Fact]
    public async Task QueryTelemetry_OnConsecutiveFailures_ShouldStopAndPromptRecovery()
    {
        var vm = new MainViewModel();
        vm.IsRecording = true;

        // 模擬連續 3 次 IPC 輪詢逾時/失敗 (核心進程無回應或已 Crash)
        await vm.QueryTelemetryAsync();
        Assert.True(vm.IsRecording, "第 1 次失敗不得貿然判斷中斷");

        await vm.QueryTelemetryAsync();
        Assert.True(vm.IsRecording, "第 2 次失敗不得貿然判斷中斷");

        await vm.QueryTelemetryAsync();
        // 驗收：第 3 次失敗觸發看門狗防護，狀態切為非錄影中，且發出修復救援警告提示！
        Assert.False(vm.IsRecording, "連續 3 次連線失敗時應自動安全收斂停止狀態");
        Assert.Contains("核心無預警中斷", vm.StatusMessage);
    }
}
