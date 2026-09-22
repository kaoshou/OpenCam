// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Platform.Windows.Display;
using ScreenRecorder.Platform.Windows.Audio;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Recovery;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;

// Removed duplicate usings
using ScreenRecorder.Platform.Windows.Hotkey;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Infrastructure.Settings;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Services;
using ScreenRecorder.UI.Views;
using Serilog;

namespace ScreenRecorder.UI.ViewModels;

internal enum RecoveryOutcome
{
    NoRecoverable,
    Success,
    PartialSuccess,
    Failed
}

public partial class MainViewModel : ObservableObject
{
    public LanguageManager Strings => LanguageManager.Instance;

    [RelayCommand]
    public void ToggleLanguage()
    {
        Strings.ToggleLanguage();
        PersistUserSettings();
    }

    private NamedPipeIpcClient _ipcClient;
    private readonly DispatcherTimer _telemetryTimer;
    private readonly IRecordingRecoveryService _recoveryService;
    private readonly IStorageService _storageService;
    private readonly IDisplayService _displayService;
    private readonly IAudioDeviceService _audioDeviceService;
    private readonly ISettingsService _settingsService;
    private readonly IEncoderDetector _encoderDetector;
    private readonly IGlobalHotkeyService? _globalHotkeyService;
    private readonly IMacOsScreenCapturePermissionService? _macOsScreenCapturePermissionService;
    private Process? _recorderProcess;
    private int _telemetryPollInFlight;

    public ISettingsService SettingsService => _settingsService;
    private bool _openFolderOnFinished = false;
    private bool _deleteWorkingFileAfterRemux = false;
    private uint _startStopHotkeyModifiers = 0;
    private string _startStopHotkeyKey = "F9";
    private uint _pauseResumeHotkeyModifiers = 0;
    private string _pauseResumeHotkeyKey = "F10";
    private string _videoQualityPreset = "Standard";
    private double _diskWarningThresholdGb = 2.0;
    private double _diskCriticalThresholdMb = 500.0;
    private long _activeDiskWarningThresholdBytes = RecordingConfiguration.DefaultDiskWarningThresholdBytes;

    private bool _isLoadingSettings = true;
    private bool _isDiskSpaceWarningActive;

    public event EventHandler? RequestMinimizeWindow;
    public event EventHandler? RequestRestoreWindow;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPreparing;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _minimizeOnRecord;

    [ObservableProperty]
    private string _statusMessage = LanguageManager.Instance["StatusReady"];

    [ObservableProperty]
    private string _elapsedTimeText = "00:00:00";

    [ObservableProperty]
    private string _fileSizeText = "0 MB";

    [ObservableProperty]
    private string _diskRemainingText = "-- GB";

    [ObservableProperty]
    private bool _isMonitorSelected = true;

    [ObservableProperty]
    private bool _isCustomRegion;

    [ObservableProperty]
    private int _regionX = 100;

    [ObservableProperty]
    private int _regionY = 100;

    [ObservableProperty]
    private int _regionWidth = 1280;

    [ObservableProperty]
    private int _regionHeight = 720;

    [ObservableProperty]
    private int _selectedFps = 30;

    public ObservableCollection<int> FpsOptions { get; } = new() { 30, 60 };

    public record EncoderOption(HardwareEncoderType Type, string DisplayName);
    public ObservableCollection<EncoderOption> AvailableEncoders { get; } = new();

    [ObservableProperty]
    private EncoderOption? _selectedEncoder;

    public record CursorEffectOption(CursorEffectMode Mode, string DisplayName);
    public ObservableCollection<CursorEffectOption> AvailableCursorEffects { get; } = new();

    [ObservableProperty]
    private CursorEffectOption? _selectedCursorEffect;

    public ObservableCollection<MonitorDisplayOption> AvailableMonitors { get; } = new();

    [ObservableProperty]
    private MonitorDisplayOption? _selectedMonitor;

    public ObservableCollection<AudioDeviceOption> AvailableMicrophones { get; } = new();

    [ObservableProperty]
    private AudioDeviceOption? _selectedMicrophone;

    [ObservableProperty]
    private bool _recordSystemAudio = true;

    [ObservableProperty]
    private bool _recordMicrophone = false;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private string? _lastOutputFilePath;

    [ObservableProperty]
    private int _recoverableSessionCount;

    [ObservableProperty]
    private bool _isRecovering;

    public bool CanStartRecording => CanStartRecordingForState(
        IsRecording,
        IsPaused,
        IsPreparing,
        IsRecovering);
    public bool CanStopRecording => (IsRecording || IsPaused) && !IsPreparing;
    public bool CanPauseOrResume => (IsRecording || IsPaused) && !IsPreparing;
    public bool CanRecoverSessions => CanRecoverSessionsForState(
        IsRecording,
        IsPaused,
        IsPreparing,
        IsRecovering);
    public bool CanEditRecordingSettings => CanEditRecordingSettingsForState(
        IsRecording,
        IsPaused,
        IsPreparing,
        IsRecovering);
    public bool CanEditPausedSettings => CanEditPausedSettingsForState(
        IsRecording,
        IsPaused,
        IsPreparing,
        IsRecovering);
    public bool CanSelectMonitor => CanEditRecordingSettings && IsMonitorSelected;
    public bool CanConfigureCustomRegion => CanEditRecordingSettings && IsCustomRegion;
    public bool CanSelectMicrophone => CanEditPausedSettings && RecordMicrophone;
    public bool SupportsSystemAudio => SupportsSystemAudioOnPlatform(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        Environment.OSVersion.Version,
        AppContext.BaseDirectory);
    public bool CanRecordSystemAudio =>
        SupportsSystemAudio && CanEditPausedSettings;
    public string SystemAudioLabel => Strings[
        SupportsSystemAudio ? "AudioSystem" : "AudioSystemUnsupportedMac"];

    internal static bool SupportsSystemAudioOnPlatform(
        bool isWindows,
        bool isMacOS,
        Version osVersion,
        string baseDirectory) =>
        isWindows ||
        (isMacOS && MacOsSystemAudioSupport.IsSupported(
            osVersion,
            baseDirectory));

    internal static bool CanStartRecordingForState(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool isRecovering) =>
        !isRecording && !isPaused && !isPreparing && !isRecovering;

    internal static bool ShouldBlockApplicationClose(
        bool isRecording,
        bool isPaused,
        bool isPreparing) =>
        isRecording || isPaused || isPreparing;

    internal static (bool IsRecording, bool IsPaused) ResolveStateAfterStopResponse(
        bool stopSucceeded,
        bool wasRecording,
        bool wasPaused) =>
        stopSucceeded
            ? (false, false)
            : (wasRecording, wasPaused);

    public bool IsApplicationCloseBlocked => ShouldBlockApplicationClose(
        IsRecording,
        IsPaused,
        IsPreparing);

    internal static bool CanEditRecordingSettingsForState(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool isRecovering = false) =>
        !isRecording && !isPaused && !isPreparing && !isRecovering;

    internal static bool CanEditPausedSettingsForState(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool isRecovering = false) =>
        !isRecording && !isPreparing && !isRecovering;

    internal static bool CanRecoverSessionsForState(
        bool isRecording,
        bool isPaused,
        bool isPreparing,
        bool isRecovering) =>
        !isRecording && !isPaused && !isPreparing && !isRecovering;

    internal static RecoveryOutcome ResolveRecoveryOutcome(
        int recoveredCount,
        int partialCount,
        int failedCount) =>
        (recoveredCount, partialCount, failedCount) switch
        {
            (0, 0, 0) => RecoveryOutcome.NoRecoverable,
            (> 0, 0, 0) => RecoveryOutcome.Success,
            (_, > 0, _) => RecoveryOutcome.PartialSuccess,
            (> 0, 0, > 0) => RecoveryOutcome.PartialSuccess,
            _ => RecoveryOutcome.Failed
        };

    public string TimerForeground => IsPaused ? "#F59E0B" : (IsRecording ? "#34D399" : "#475569");

    public string StatusBadgeColor => IsPaused ? "#F59E0B" : (IsRecording ? "#10B981" : "#64748B");
    public string StatusBadgeBoxShadow => IsPaused ? "0 0 8 #F59E0B" : (IsRecording ? "0 0 8 #10B981" : "0 0 0 #00000000");
    public string StatusBadgeTextColor => IsPaused ? "#FBBF24" : (IsRecording ? "#34D399" : "#94A3B8");
    public string StatusBadgeText => IsPaused ? Strings["StatusPaused"] : (IsRecording ? Strings["StatusRecording"] : Strings["StatusIdle"]);

    public static string FormatHotkey(uint modifiers, string key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if (!string.IsNullOrWhiteSpace(key))
        {
            parts.Add(key);
        }
        return parts.Count > 0 ? string.Join(" + ", parts) : key;
    }

    internal static AudioSourceType ResolveAudioSource(
        bool supportsSystemAudio,
        bool recordSystemAudio,
        bool recordMicrophone)
    {
        return (supportsSystemAudio && recordSystemAudio, recordMicrophone) switch
        {
            (true, true) => AudioSourceType.SystemAndMicrophone,
            (true, false) => AudioSourceType.SystemOnly,
            (false, true) => AudioSourceType.MicrophoneOnly,
            _ => AudioSourceType.None
        };
    }

    public string StartStopHotkeyText => FormatHotkey(_startStopHotkeyModifiers, _startStopHotkeyKey);
    public string PauseResumeHotkeyText => FormatHotkey(_pauseResumeHotkeyModifiers, _pauseResumeHotkeyKey);

    public string StartRecordingTooltipText => Strings.GetFormatted("TooltipStartFormat", StartStopHotkeyText);
    public string StopRecordingTooltipText => Strings.GetFormatted("TooltipStopFormat", StartStopHotkeyText);

    public string PauseResumeButtonText => IsPaused ? Strings["ResumeRecording"] : Strings["PauseRecording"];
    public string PauseResumeTooltipText => IsPaused
        ? Strings.GetFormatted("TooltipResumeFormat", PauseResumeHotkeyText)
        : Strings.GetFormatted("TooltipPauseFormat", PauseResumeHotkeyText);

    public void RefreshHotkeyTooltips()
    {
        OnPropertyChanged(nameof(StartStopHotkeyText));
        OnPropertyChanged(nameof(PauseResumeHotkeyText));
        OnPropertyChanged(nameof(StartRecordingTooltipText));
        OnPropertyChanged(nameof(StopRecordingTooltipText));
        OnPropertyChanged(nameof(PauseResumeTooltipText));
        OnPropertyChanged(nameof(PauseResumeButtonText));
    }

    public bool MatchesStartStopHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        return MatchesHotkey(key, modifiers, _startStopHotkeyModifiers, _startStopHotkeyKey);
    }

    public bool MatchesPauseResumeHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        return MatchesHotkey(key, modifiers, _pauseResumeHotkeyModifiers, _pauseResumeHotkeyKey);
    }

    private static bool MatchesHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, uint expectedModifiers, string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(expectedKey)) return false;

        if (!key.ToString().Equals(expectedKey, StringComparison.OrdinalIgnoreCase))
            return false;

        bool expectedCtrl = (expectedModifiers & 2) != 0;
        bool expectedAlt = (expectedModifiers & 1) != 0;
        bool expectedShift = (expectedModifiers & 4) != 0;

        bool actualCtrl = (modifiers & Avalonia.Input.KeyModifiers.Control) != 0;
        bool actualAlt = (modifiers & Avalonia.Input.KeyModifiers.Alt) != 0;
        bool actualShift = (modifiers & Avalonia.Input.KeyModifiers.Shift) != 0;

        return expectedCtrl == actualCtrl && expectedAlt == actualAlt && expectedShift == actualShift;
    }

    public string CustomRegionDisplayText => Strings.GetFormatted("RegionSizeFormat", RegionWidth, RegionHeight, RegionX, RegionY);

    public string CurrentTargetSummary
    {
        get
        {
            if (IsCustomRegion) return Strings.GetFormatted("TargetCustomRegion", RegionWidth, RegionHeight, RegionX, RegionY);
            return Strings.GetFormatted("TargetMonitor", SelectedMonitor?.DisplayName ?? "1");
        }
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        OnPropertyChanged(nameof(CanPauseOrResume));
        OnPropertyChanged(nameof(CanRecoverSessions));
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditRecordingSettings));
        OnPropertyChanged(nameof(CanEditPausedSettings));
        OnPropertyChanged(nameof(CanSelectMonitor));
        OnPropertyChanged(nameof(CanConfigureCustomRegion));
        OnPropertyChanged(nameof(CanSelectMicrophone));
        OnPropertyChanged(nameof(CanRecordSystemAudio));
        OnPropertyChanged(nameof(TimerForeground));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(StatusBadgeBoxShadow));
        OnPropertyChanged(nameof(StatusBadgeTextColor));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        OnPropertyChanged(nameof(PauseResumeTooltipText));
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        OnPropertyChanged(nameof(CanPauseOrResume));
        OnPropertyChanged(nameof(CanRecoverSessions));
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditRecordingSettings));
        OnPropertyChanged(nameof(CanEditPausedSettings));
        OnPropertyChanged(nameof(CanSelectMonitor));
        OnPropertyChanged(nameof(CanConfigureCustomRegion));
        OnPropertyChanged(nameof(CanSelectMicrophone));
        OnPropertyChanged(nameof(CanRecordSystemAudio));
        OnPropertyChanged(nameof(TimerForeground));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(StatusBadgeBoxShadow));
        OnPropertyChanged(nameof(StatusBadgeTextColor));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        OnPropertyChanged(nameof(PauseResumeTooltipText));
    }

    partial void OnIsPreparingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        OnPropertyChanged(nameof(CanPauseOrResume));
        OnPropertyChanged(nameof(CanRecoverSessions));
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditRecordingSettings));
        OnPropertyChanged(nameof(CanEditPausedSettings));
        OnPropertyChanged(nameof(CanSelectMonitor));
        OnPropertyChanged(nameof(CanConfigureCustomRegion));
        OnPropertyChanged(nameof(CanSelectMicrophone));
        OnPropertyChanged(nameof(CanRecordSystemAudio));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(StatusBadgeBoxShadow));
        OnPropertyChanged(nameof(StatusBadgeTextColor));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        OnPropertyChanged(nameof(PauseResumeTooltipText));
    }

    partial void OnIsRecoveringChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanEditRecordingSettings));
        OnPropertyChanged(nameof(CanEditPausedSettings));
        OnPropertyChanged(nameof(CanSelectMonitor));
        OnPropertyChanged(nameof(CanConfigureCustomRegion));
        OnPropertyChanged(nameof(CanSelectMicrophone));
        OnPropertyChanged(nameof(CanRecordSystemAudio));
        OnPropertyChanged(nameof(CanRecoverSessions));
        RecoverSessionsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMonitorSelectedChanged(bool value)
    {
        if (value)
        {
            IsCustomRegion = false;
        }
        OnPropertyChanged(nameof(CanSelectMonitor));
        OnPropertyChanged(nameof(CurrentTargetSummary));
    }

    partial void OnIsCustomRegionChanged(bool value)
    {
        if (value)
        {
            IsMonitorSelected = false;
        }
        OnPropertyChanged(nameof(CanConfigureCustomRegion));
        OnPropertyChanged(nameof(CurrentTargetSummary));
    }

    partial void OnSelectedMonitorChanged(MonitorDisplayOption? value)
    {
        OnPropertyChanged(nameof(CurrentTargetSummary));
        PersistUserSettings();
    }

    partial void OnRegionWidthChanged(int value) => OnCustomRegionChanged();
    partial void OnRegionHeightChanged(int value) => OnCustomRegionChanged();
    partial void OnRegionXChanged(int value) => OnCustomRegionChanged();
    partial void OnRegionYChanged(int value) => OnCustomRegionChanged();

    private void OnCustomRegionChanged()
    {
        OnPropertyChanged(nameof(CurrentTargetSummary));
        OnPropertyChanged(nameof(CustomRegionDisplayText));
        PersistUserSettings();
    }

    partial void OnSelectedFpsChanged(int value) => PersistUserSettings();
    partial void OnSelectedEncoderChanged(EncoderOption? value) => PersistUserSettings();
    partial void OnSelectedCursorEffectChanged(CursorEffectOption? value)
    {
        PersistUserSettings();
    }
    partial void OnMinimizeOnRecordChanged(bool value) => PersistUserSettings();

    partial void OnRecordSystemAudioChanged(bool value)
    {
        PersistUserSettings();
    }

    partial void OnRecordMicrophoneChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectMicrophone));
        PersistUserSettings();
    }

    partial void OnSelectedMicrophoneChanged(AudioDeviceOption? value)
    {
        PersistUserSettings();
    }

    private RecordingConfiguration BuildPausedConfiguration() =>
        new()
        {
            AudioSource = ResolveAudioSource(
                SupportsSystemAudio,
                RecordSystemAudio,
                RecordMicrophone),
            MicrophoneDeviceId = RecordMicrophone ? SelectedMicrophone?.Id : null,
            SystemAudioDeviceId = null,
            CursorEffect = SelectedCursorEffect?.Mode ?? CursorEffectMode.Default
        };

    public static RecordingConfiguration ApplyRuntimeSettings(
        RecordingConfiguration config,
        string videoQualityPreset,
        bool deleteWorkingFileAfterRemux,
        double diskWarningThresholdGb,
        double diskCriticalThresholdMb)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.VideoQualityPreset = videoQualityPreset;
        config.DeleteWorkingFileAfterSuccessfulRemux = deleteWorkingFileAfterRemux;
        config.DiskWarningThresholdBytes = ConvertThresholdToBytes(
            diskWarningThresholdGb,
            1024d * 1024d * 1024d,
            RecordingConfiguration.DefaultDiskWarningThresholdBytes);
        config.DiskCriticalThresholdBytes = ConvertThresholdToBytes(
            diskCriticalThresholdMb,
            1024d * 1024d,
            RecordingConfiguration.DefaultDiskCriticalThresholdBytes);
        config.NormalizeDiskGuardThresholds();
        return config;
    }

    public static bool IsDiskSpaceWarning(
        RecordingState state,
        long availableBytes,
        long warningThresholdBytes) =>
        state is RecordingState.Recording or RecordingState.Paused &&
        availableBytes >= 0 &&
        warningThresholdBytes > 0 &&
        availableBytes <= warningThresholdBytes;

    private static long ConvertThresholdToBytes(
        double value,
        double multiplier,
        long fallback)
    {
        if (!double.IsFinite(value) || value <= 0 || value > long.MaxValue / multiplier)
        {
            return fallback;
        }

        return (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
    }

    public MainViewModel()
    {
        _storageService = new StorageService();
        _outputDirectory = _storageService.GetDefaultRecordingsPath();
        _settingsService = new SettingsService();
        
        IFFmpegPlatformProvider ffmpegPlatformProvider = OperatingSystem.IsWindows() ? new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider() : new ScreenRecorder.Platform.macOS.MacOsFFmpegProvider();

        _encoderDetector = new FFmpegEncoderDetector(ffmpegPlatformProvider);
        _displayService = OperatingSystem.IsWindows() ? new ScreenRecorder.Platform.Windows.Display.WindowsDisplayService() : new ScreenRecorder.Platform.macOS.MacOsDisplayService();
        _audioDeviceService = OperatingSystem.IsWindows() ? new ScreenRecorder.Platform.Windows.Audio.WindowsAudioDeviceService() : new ScreenRecorder.Platform.macOS.MacOsAudioDeviceService();
        _macOsScreenCapturePermissionService = OperatingSystem.IsMacOS()
            ? new MacOsScreenCapturePermissionService()
            : null;

        _ipcClient = new NamedPipeIpcClient(NamedPipeConstants.PipeBaseName);

        // 注入 Recovery 服務
        _recoveryService = new RecordingRecoveryService(
            new JsonRecordingSessionStore(),
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            _storageService);

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _telemetryTimer.Tick += OnTelemetryTimerTick;

        // 動態偵測所有實體顯示器
        Strings.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Strings));
            OnPropertyChanged(nameof(CurrentTargetSummary));
            OnPropertyChanged(nameof(CustomRegionDisplayText));
            RefreshHotkeyTooltips();
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(SystemAudioLabel));
            LoadMonitors();
            RefreshEncoderDisplayNames();
            RefreshCursorEffectDisplayNames();
            if (!IsRecording && !IsPreparing)
            {
                if (RecoverableSessionCount > 0)
                {
                    StatusMessage = Strings.GetFormatted("StatusRecoverableDetected", RecoverableSessionCount);
                }
                else
                {
                    StatusMessage = Strings["StatusReady"];
                }
            }
        };

        // 動態偵測所有音訊輸入裝置
        LoadMonitors();

        // 非同步掃描未完成之錄影
        LoadMicrophones();

        // 非同步掃描未完成之錄影
        LoadCursorEffects(CursorEffectMode.Default);

        // 非同步掃描未完成之錄影
        AvailableEncoders.Clear();
        AvailableEncoders.Add(new EncoderOption(HardwareEncoderType.Auto, Strings["EncoderAuto"]));
        AvailableEncoders.Add(new EncoderOption(HardwareEncoderType.SoftwareCpu, Strings["EncoderCpu"]));
        SelectedEncoder = AvailableEncoders[0];

        // 載入使用者持久化設定 (包含介面語系、FPS、音訊、儲存位置與快捷鍵)
        LoadUserSettings();

        // 非同步掃描未完成之錄影
        _ = CheckRecoverableSessionsAsync();

        // 立即探測目標存放磁碟之剩餘空間 (更新介面顯示 -- GB)
        UpdateDiskSpaceRemaining();

        // 註冊 Windows 全域快捷鍵 (預設 F9 開始/停止，F10 暫停/繼續)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _globalHotkeyService = new WindowsGlobalHotkeyService();
                _globalHotkeyService.HotkeyTriggered += async (s, hotkeyId) =>
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (hotkeyId == 9001) // F9: 開始 / 停止
                        {
                            if (CanStartRecording)
                            {
                                await StartRecordingAsync();
                            }
                            else if ((IsRecording || IsPaused) && !IsPreparing)
                            {
                                await StopRecordingAsync();
                            }
                        }
                        else if (hotkeyId == 9002) // F10: 暫停 / 繼續
                        {
                            if ((IsRecording || IsPaused) && !IsPreparing)
                            {
                                await TogglePauseResumeAsync();
                            }
                        }
                    });
                };
                // 預設先註冊 F9 與 F10，待 LoadUserSettings 讀取使用者設定後會自動更新註冊
                _globalHotkeyService.RegisterHotkey(9001, 0x78); // VK_F9
                _globalHotkeyService.RegisterHotkey(9002, 0x79); // VK_F10
            }
            catch { }
        }
    }

    private async void LoadUserSettings()
    {
        try
        {
            var s = await _settingsService.LoadSettingsAsync();
            SelectedFps = s.Fps;
            RecordSystemAudio = SupportsSystemAudio && s.RecordSystemAudio;
            RecordMicrophone = s.RecordMicrophone;
            MinimizeOnRecord = s.MinimizeOnRecord;
            Strings.CurrentLanguage = s.Language;

            _startStopHotkeyModifiers = s.StartStopHotkeyModifiers;
            _startStopHotkeyKey = s.StartStopHotkeyKey;
            _pauseResumeHotkeyModifiers = s.PauseResumeHotkeyModifiers;
            _pauseResumeHotkeyKey = s.PauseResumeHotkeyKey;
            _videoQualityPreset = s.VideoQualityPreset;
            _openFolderOnFinished = s.OpenFolderOnFinished;
            _deleteWorkingFileAfterRemux = s.DeleteWorkingFileAfterSuccessfulRemux;
            _diskWarningThresholdGb = s.DiskWarningThresholdGb;
            _diskCriticalThresholdMb = s.DiskCriticalThresholdMb;

            RegisterHotkeys(s);
            RefreshHotkeyTooltips();

            if (!string.IsNullOrWhiteSpace(s.CustomOutputDirectory) && Directory.Exists(s.CustomOutputDirectory))
            {
                OutputDirectory = s.CustomOutputDirectory;
                UpdateDiskSpaceRemaining(s.CustomOutputDirectory);
            }

            RegionX = s.RegionX;
            RegionY = s.RegionY;
            RegionWidth = s.RegionWidth;
            RegionHeight = s.RegionHeight;

            if (s.CaptureRangeMode == "CustomRegion")
            {
                IsCustomRegion = true;
                IsMonitorSelected = false;
            }
            else if (s.CaptureRangeMode == "Monitor" || s.CaptureRangeMode == "FullScreen") // fallback FullScreen to Monitor
            {
                IsMonitorSelected = true;
                IsCustomRegion = false;
                if (s.SelectedMonitorIndex >= 0 && s.SelectedMonitorIndex < AvailableMonitors.Count)
                {
                    SelectedMonitor = AvailableMonitors[s.SelectedMonitorIndex];
                }
            }

            if (!string.IsNullOrWhiteSpace(s.SelectedMicrophoneId))
            {
                var match = AvailableMicrophones.FirstOrDefault(m => m.Id == s.SelectedMicrophoneId);
                if (match != null) SelectedMicrophone = match;
            }

            await LoadEncodersAsync(s.EncoderType);
            LoadCursorEffects(s.CursorEffect);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "載入使用者設定失敗，將使用預設值");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void RefreshCursorEffectDisplayNames()
    {
        var currentMode = SelectedCursorEffect?.Mode ?? CursorEffectMode.Default;
        var updated = new List<CursorEffectOption>();
        foreach (var opt in AvailableCursorEffects)
        {
            string label = opt.Mode switch
            {
                CursorEffectMode.Default => Strings["CursorDefault"],
                CursorEffectMode.HighlightHalo => Strings["CursorHighlight"],
                CursorEffectMode.HaloWithClickRipple => Strings["CursorClickRipple"],
                CursorEffectMode.Hidden => Strings["CursorHidden"],
                _ => opt.DisplayName
            };
            updated.Add(new CursorEffectOption(opt.Mode, label));
        }

        AvailableCursorEffects.Clear();
        foreach (var opt in updated)
        {
            AvailableCursorEffects.Add(opt);
        }
        SelectedCursorEffect = AvailableCursorEffects.FirstOrDefault(e => e.Mode == currentMode) ?? AvailableCursorEffects.FirstOrDefault();
    }

    private void LoadCursorEffects(CursorEffectMode preferred)
    {
        AvailableCursorEffects.Clear();
        AvailableCursorEffects.Add(new CursorEffectOption(CursorEffectMode.Default, Strings["CursorDefault"]));
        AvailableCursorEffects.Add(new CursorEffectOption(CursorEffectMode.HighlightHalo, Strings["CursorHighlight"]));
        AvailableCursorEffects.Add(new CursorEffectOption(CursorEffectMode.HaloWithClickRipple, Strings["CursorClickRipple"]));
        AvailableCursorEffects.Add(new CursorEffectOption(CursorEffectMode.Hidden, Strings["CursorHidden"]));

        SelectedCursorEffect = AvailableCursorEffects.FirstOrDefault(c => c.Mode == preferred)
                               ?? AvailableCursorEffects.FirstOrDefault();
    }

    private void RefreshEncoderDisplayNames()
    {
        var currentType = SelectedEncoder?.Type ?? HardwareEncoderType.Auto;
        var updated = new List<EncoderOption>();
        foreach (var opt in AvailableEncoders)
        {
            string label = opt.Type switch
            {
                HardwareEncoderType.Auto => Strings["EncoderAuto"],
                HardwareEncoderType.SoftwareCpu => Strings["EncoderCpu"],
                HardwareEncoderType.NvidiaNvenc => Strings["EncoderNvenc"],
                HardwareEncoderType.IntelQsv => Strings["EncoderQsv"],
                HardwareEncoderType.AmdAmf => Strings["EncoderAmf"],
                _ => opt.DisplayName
            };
            updated.Add(new EncoderOption(opt.Type, label));
        }

        if (updated.Count == 0)
        {
            updated.Add(new EncoderOption(HardwareEncoderType.Auto, Strings["EncoderAuto"]));
            updated.Add(new EncoderOption(HardwareEncoderType.SoftwareCpu, Strings["EncoderCpu"]));
        }

        AvailableEncoders.Clear();
        foreach (var opt in updated)
        {
            AvailableEncoders.Add(opt);
        }
        SelectedEncoder = AvailableEncoders.FirstOrDefault(e => e.Type == currentType) ?? AvailableEncoders.FirstOrDefault();
    }

    private async Task LoadEncodersAsync(HardwareEncoderType preferred)
    {
        try
        {
            var capabilities = await _encoderDetector.DetectAvailableEncodersAsync();
            AvailableEncoders.Clear();
            foreach (var cap in capabilities.Where(c => c.IsAvailable))
            {
                string label = cap.Type switch
                {
                    HardwareEncoderType.Auto => Strings["EncoderAuto"],
                    HardwareEncoderType.SoftwareCpu => Strings["EncoderCpu"],
                    HardwareEncoderType.NvidiaNvenc => Strings["EncoderNvenc"],
                    HardwareEncoderType.IntelQsv => Strings["EncoderQsv"],
                    HardwareEncoderType.AmdAmf => Strings["EncoderAmf"],
                    _ => cap.DisplayName
                };
                AvailableEncoders.Add(new EncoderOption(cap.Type, label));
            }

            SelectedEncoder = AvailableEncoders.FirstOrDefault(e => e.Type == preferred) 
                              ?? AvailableEncoders.FirstOrDefault();
        }
        catch
        {
            AvailableEncoders.Clear();
            AvailableEncoders.Add(new EncoderOption(HardwareEncoderType.Auto, Strings["EncoderAuto"]));
            AvailableEncoders.Add(new EncoderOption(HardwareEncoderType.SoftwareCpu, Strings["EncoderCpu"]));
            SelectedEncoder = AvailableEncoders.First();
        }
    }

    public void PersistUserSettings()
    {
        if (_isLoadingSettings) return;
        try
        {
            var rangeMode = "Monitor";
            if (IsCustomRegion) rangeMode = "CustomRegion";

            var settings = new UserSettings
            {
                Fps = SelectedFps,
                Language = Strings.CurrentLanguage,
                EncoderType = SelectedEncoder?.Type ?? HardwareEncoderType.Auto,
                CursorEffect = SelectedCursorEffect?.Mode ?? CursorEffectMode.Default,
                RecordSystemAudio = RecordSystemAudio,
                RecordMicrophone = RecordMicrophone,
                SelectedMicrophoneId = SelectedMicrophone?.Id,
                CaptureRangeMode = rangeMode,
                SelectedMonitorIndex = SelectedMonitor?.Index ?? 0,
                RegionX = RegionX,
                RegionY = RegionY,
                RegionWidth = RegionWidth,
                RegionHeight = RegionHeight,
                CustomOutputDirectory = OutputDirectory,
                MinimizeOnRecord = MinimizeOnRecord,
                StartStopHotkeyModifiers = _startStopHotkeyModifiers,
                StartStopHotkeyKey = _startStopHotkeyKey,
                PauseResumeHotkeyModifiers = _pauseResumeHotkeyModifiers,
                PauseResumeHotkeyKey = _pauseResumeHotkeyKey,
                VideoQualityPreset = _videoQualityPreset,
                OpenFolderOnFinished = _openFolderOnFinished,
                DeleteWorkingFileAfterSuccessfulRemux = _deleteWorkingFileAfterRemux,
                DiskWarningThresholdGb = _diskWarningThresholdGb,
                DiskCriticalThresholdMb = _diskCriticalThresholdMb
            };
            _ = Task.Run(() => _settingsService.SaveSettingsAsync(settings));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "建立使用者設定快照失敗");
        }
    }

    public void ApplyNewSettings(UserSettings newSettings)
    {
        _startStopHotkeyModifiers = newSettings.StartStopHotkeyModifiers;
        _startStopHotkeyKey = newSettings.StartStopHotkeyKey;
        _pauseResumeHotkeyModifiers = newSettings.PauseResumeHotkeyModifiers;
        _pauseResumeHotkeyKey = newSettings.PauseResumeHotkeyKey;
        _videoQualityPreset = newSettings.VideoQualityPreset;
        _openFolderOnFinished = newSettings.OpenFolderOnFinished;
        _deleteWorkingFileAfterRemux = newSettings.DeleteWorkingFileAfterSuccessfulRemux;
        _diskWarningThresholdGb = newSettings.DiskWarningThresholdGb;
        _diskCriticalThresholdMb = newSettings.DiskCriticalThresholdMb;
        MinimizeOnRecord = newSettings.MinimizeOnRecord;

        Strings.CurrentLanguage = newSettings.Language;
        RegisterHotkeys(newSettings);
        PersistUserSettings();
        RefreshHotkeyTooltips();
    }

    private static int ResolveVirtualKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return 0;
        return keyName.ToUpperInvariant() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "R" => 0x52,
            "S" => 0x53,
            "P" => 0x50,
            "SPACE" => 0x20,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            _ => 0
        };
    }

    public void RegisterHotkeys(UserSettings s)
    {
        if (!OperatingSystem.IsWindows() || _globalHotkeyService == null) return;

        try
        {
            _globalHotkeyService.UnregisterHotkey(9001);
            _globalHotkeyService.UnregisterHotkey(9002);

            int vkStart = ResolveVirtualKey(s.StartStopHotkeyKey);
            if (vkStart > 0)
            {
                _globalHotkeyService.RegisterHotkey(9001, vkStart, s.StartStopHotkeyModifiers);
            }

            int vkPause = ResolveVirtualKey(s.PauseResumeHotkeyKey);
            if (vkPause > 0)
            {
                _globalHotkeyService.RegisterHotkey(9002, vkPause, s.PauseResumeHotkeyModifiers);
            }
        }
        catch { }
    }

    private void LoadMonitors()
    {
        try
        {
            var monitors = _displayService.GetMonitors();
            var prevIndex = SelectedMonitor?.Index ?? 0;
            AvailableMonitors.Clear();
            foreach (var m in monitors)
            {
                var title = m.IsPrimary 
                    ? Strings.GetFormatted("MonitorPrimaryFormat", m.Index + 1)
                    : Strings.GetFormatted("MonitorSecondaryFormat", m.Index + 1);
                AvailableMonitors.Add(new MonitorDisplayOption(m.Index, title, m.Bounds.Width, m.Bounds.Height, m.IsPrimary));
            }

            if (AvailableMonitors.Count > 0)
            {
                SelectedMonitor = AvailableMonitors.FirstOrDefault(m => m.Index == prevIndex)
                                  ?? AvailableMonitors.FirstOrDefault(m => m.IsPrimary) 
                                  ?? AvailableMonitors[0];
            }
        }
        catch
        {
            if (AvailableMonitors.Count == 0)
            {
                AvailableMonitors.Add(new MonitorDisplayOption(0, Strings.GetFormatted("MonitorPrimaryFormat", 1), 1920, 1080, true));
                SelectedMonitor = AvailableMonitors[0];
            }
        }
    }

    private void LoadMicrophones()
    {
        try
        {
            var mics = _audioDeviceService.GetRecordingDevices();
            AvailableMicrophones.Clear();
            foreach (var mic in mics)
            {
                AvailableMicrophones.Add(mic);
            }

            if (AvailableMicrophones.Count > 0)
            {
                SelectedMicrophone = AvailableMicrophones.FirstOrDefault(m => m.IsDefault) ?? AvailableMicrophones[0];
            }
        }
        catch
        {
            if (AvailableMicrophones.Count == 0)
            {
                var defaultMic = new AudioDeviceOption("default_mic", Strings["DefaultMicOption"], true, true);
                AvailableMicrophones.Add(defaultMic);
                SelectedMicrophone = defaultMic;
            }
        }
    }

    public async Task CheckRecoverableSessionsAsync()
    {
        try
        {
            var sessions = await _recoveryService.ScanForRecoverableSessionsAsync(OutputDirectory);
            RecoverableSessionCount = sessions.Count;
            if (RecoverableSessionCount > 0)
            {
                StatusMessage = Strings.GetFormatted("StatusRecoverableDetected", RecoverableSessionCount);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "掃描可救援錄影工作階段失敗: {OutputDirectory}", OutputDirectory);
        }
    }

    [RelayCommand]
    public async Task StartRecordingAsync()
    {
        if (!CanStartRecording) return;

        IsPreparing = true;
        StatusMessage = Strings["StatusInitializing"];

        try
        {
            if (_macOsScreenCapturePermissionService is not null &&
                !_macOsScreenCapturePermissionService.HasPermission() &&
                !_macOsScreenCapturePermissionService.RequestPermission())
            {
                StatusMessage = Strings["StatusScreenPermissionRequired"];
                return;
            }

            await EnsureRecorderProcessAsync();

            var captureSource = CaptureSourceType.Monitor;
            if (IsCustomRegion) captureSource = CaptureSourceType.CustomRegion;

            var config = ApplyRuntimeSettings(new RecordingConfiguration
            {
                Fps = SelectedFps,
                EncoderType = SelectedEncoder?.Type ?? HardwareEncoderType.Auto,
                CursorEffect = SelectedCursorEffect?.Mode ?? CursorEffectMode.Default,
                OutputDirectory = OutputDirectory,
                CaptureSource = captureSource,
                MonitorIndex = SelectedMonitor?.Index ?? 0,
                Region = new CaptureRegion(RegionX, RegionY, RegionWidth, RegionHeight),
                MicrophoneDeviceId = RecordMicrophone ? SelectedMicrophone?.Id : null,
                AudioSource = ResolveAudioSource(
                    SupportsSystemAudio,
                    RecordSystemAudio,
                    RecordMicrophone)
            },
            _videoQualityPreset,
            _deleteWorkingFileAfterRemux,
            _diskWarningThresholdGb,
            _diskCriticalThresholdMb);
            _activeDiskWarningThresholdBytes = config.DiskWarningThresholdBytes;

            var response = await _ipcClient.SendCommandAsync("StartRecording", config, timeoutMs: 8000);

            if (response.Success)
            {
                IsRecording = true;
                IsPaused = false;
                StatusMessage = Strings["StatusRecordingActive"];
                _telemetryTimer.Start();

                if (MinimizeOnRecord)
                {
                    RequestMinimizeWindow?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                if (response.ErrorMessage?.Contains("Recording") == true)
                {
                    IsRecording = true;
                    IsPaused = false;
                    StatusMessage = Strings["StatusRecordingActive"];
                    _telemetryTimer.Start();
                    return;
                }
                StatusMessage = Strings.GetFormatted("StatusFailed", response.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Strings.GetFormatted("StatusFailed", ex.Message);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    [RelayCommand]
    public async Task TogglePauseResumeAsync()
    {
        if ((!IsRecording && !IsPaused) || IsPreparing) return;

        if (IsPaused)
        {
            IsPreparing = true;
            try
            {
                StatusMessage = Strings["StatusResuming"];

                // Send one authoritative snapshot immediately before resume.
                // This avoids racing fire-and-forget updates when the user
                // changes several paused settings in quick succession.
                var updateResponse = await _ipcClient.SendCommandAsync(
                    "UpdatePausedConfiguration",
                    BuildPausedConfiguration(),
                    timeoutMs: 8000);
                if (!updateResponse.Success)
                {
                    StatusMessage = Strings.GetFormatted(
                        "StatusFailed",
                        updateResponse.ErrorMessage ?? string.Empty);
                    return;
                }

                var response = await _ipcClient.SendCommandAsync(
                    "ResumeRecording",
                    new { },
                    timeoutMs: 8000);
                if (response.Success)
                {
                    IsRecording = true;
                    IsPaused = false;
                    StatusMessage = Strings["StatusRecordingActive"];
                }
                else
                {
                    StatusMessage = Strings.GetFormatted(
                        "StatusFailed",
                        response.ErrorMessage ?? string.Empty);
                }
            }
            finally
            {
                IsPreparing = false;
            }
        }
        else if (IsRecording)
        {
            StatusMessage = Strings["StatusPausedMsg"];
            var response = await _ipcClient.SendCommandAsync("PauseRecording", new { }, timeoutMs: 8000);
            if (response.Success)
            {
                IsPaused = true;
                IsRecording = false;
                StatusMessage = Strings["StatusPausedMsg"];
            }
            else
            {
                StatusMessage = Strings.GetFormatted("StatusFailed", response.ErrorMessage ?? string.Empty);
            }
        }
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if ((!IsRecording && !IsPaused) || IsPreparing) return;

        var wasRecording = IsRecording;
        var wasPaused = IsPaused;
        IsPreparing = true;
        _telemetryTimer.Stop();
        StatusMessage = Strings["StatusStopping"];

        try
        {
            var response = await _ipcClient.SendCommandAsync(
                "StopRecording",
                new StopRecordingCommand(),
                timeoutMs: 15000);
            var nextState = ResolveStateAfterStopResponse(
                response.Success,
                wasRecording,
                wasPaused);
            IsRecording = nextState.IsRecording;
            IsPaused = nextState.IsPaused;

            if (response.Success)
            {
                LastOutputFilePath = response.SessionId;
                StatusMessage = Strings["StatusSuccess"];

                if (MinimizeOnRecord)
                {
                    RequestRestoreWindow?.Invoke(this, EventArgs.Empty);
                }

                if (_openFolderOnFinished && !string.IsNullOrEmpty(LastOutputFilePath))
                {
                    OpenOutputFolder();
                }
            }
            else
            {
                StatusMessage = Strings.GetFormatted("StatusStopFailed", response.ErrorMessage ?? string.Empty);
                _telemetryTimer.Start();
            }
        }
        finally
        {
            IsPreparing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecoverSessions))]
    public async Task RecoverSessionsAsync()
    {
        if (!CanRecoverSessions)
        {
            return;
        }

        IsRecovering = true;
        StatusMessage = Strings["StatusRecovering"];
        try
        {
            var sessions = await _recoveryService.ScanForRecoverableSessionsAsync(OutputDirectory);
            if (sessions.Count == 0)
            {
                StatusMessage = Strings["StatusNoRecoverable"];
                RecoverableSessionCount = 0;
                return;
            }

            int recoveredCount = 0;
            int partialCount = 0;
            int failedCount = 0;
            foreach (var session in sessions)
            {
                var (success, isPartial, error, mp4) = await _recoveryService.RecoverSessionAsync(
                    session.Session.WorkingDirectory);
                if (success)
                {
                    if (isPartial)
                    {
                        partialCount++;
                    }
                    else
                    {
                        recoveredCount++;
                    }
                    LastOutputFilePath = mp4;
                }
                else
                {
                    failedCount++;
                    Log.Warning(
                        "救援工作階段失敗: {SessionId}, {Error}",
                        session.Session.SessionId,
                        error ?? "未知錯誤");
                }
            }

            var remaining = await _recoveryService.ScanForRecoverableSessionsAsync(OutputDirectory);
            RecoverableSessionCount = remaining.Count;
            StatusMessage = ResolveRecoveryOutcome(recoveredCount, partialCount, failedCount) switch
            {
                RecoveryOutcome.Success => Strings.GetFormatted(
                    "StatusRecoverSuccess",
                    recoveredCount),
                RecoveryOutcome.PartialSuccess => Strings.GetFormatted(
                    "StatusRecoverPartial",
                    recoveredCount,
                    partialCount,
                    failedCount),
                RecoveryOutcome.Failed => Strings.GetFormatted(
                    "StatusRecoverFailed",
                    failedCount),
                _ => Strings["StatusNoRecoverable"]
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "掃描或執行 Crash Recovery 時發生錯誤");
            StatusMessage = Strings.GetFormatted("StatusRecoverFailed", 1);
            try
            {
                var remaining = await _recoveryService.ScanForRecoverableSessionsAsync(OutputDirectory);
                RecoverableSessionCount = remaining.Count;
            }
            catch (Exception scanException)
            {
                Log.Warning(scanException, "救援失敗後重新掃描工作階段亦失敗");
            }
        }
        finally
        {
            IsRecovering = false;
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        var (success, error) = PlatformFolderOpener.TryOpen(
            LastOutputFilePath,
            OutputDirectory);
        if (!success)
        {
            StatusMessage = Strings.GetFormatted(
                "StatusOpenFolderFailed",
                error ?? string.Empty);
        }
    }

    public void UpdateCustomRegion(int x, int y, int width, int height)
    {
        // 防呆：長寬確保至少 160x120 且為偶數 (H.264 要求偶數像素)
        width = Math.Max(160, width);
        height = Math.Max(120, height);
        if (width % 2 != 0) width--;
        if (height % 2 != 0) height--;

        var region = new CaptureRegion(x, y, width, height);
        if (OperatingSystem.IsMacOS())
        {
            var centerX = region.X + region.Width / 2;
            var centerY = region.Y + region.Height / 2;
            var monitors = _displayService.GetMonitors();
            var monitor = monitors.FirstOrDefault(candidate =>
                              centerX >= candidate.Bounds.X &&
                              centerX < candidate.Bounds.X + candidate.Bounds.Width &&
                              centerY >= candidate.Bounds.Y &&
                              centerY < candidate.Bounds.Y + candidate.Bounds.Height) ??
                          monitors.FirstOrDefault(candidate =>
                              candidate.Index == SelectedMonitor?.Index) ??
                          monitors.FirstOrDefault(candidate => candidate.IsPrimary) ??
                          monitors.FirstOrDefault();

            if (monitor is not null)
            {
                region = RegionSelectionGeometry.ClampToBounds(region, monitor.Bounds);
                SelectedMonitor = AvailableMonitors.FirstOrDefault(candidate =>
                                      candidate.Index == monitor.Index) ??
                                  SelectedMonitor;
            }
        }

        RegionX = region.X;
        RegionY = region.Y;
        RegionWidth = region.Width;
        RegionHeight = region.Height;
        IsCustomRegion = true;
        IsMonitorSelected = false;
        PersistUserSettings();
    }

    public void SetOutputDirectory(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        try
        {
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }
            OutputDirectory = newPath;
            UpdateDiskSpaceRemaining(newPath);
            StatusMessage = Strings.GetFormatted("StatusLocationUpdated", newPath);
            PersistUserSettings();
            _ = CheckRecoverableSessionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Strings.GetFormatted("StatusSetLocationFailed", ex.Message);
        }
    }

    public void UpdateDiskSpaceRemaining(string? path = null)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(path) ? OutputDirectory : path;
            if (!string.IsNullOrWhiteSpace(target))
            {
                var full = Path.GetFullPath(target);
                var root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root))
                {
                    var driveInfo = new DriveInfo(root);
                    if (driveInfo.IsReady)
                    {
                        double gb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        DiskRemainingText = $"{gb:F1} GB ({driveInfo.Name.TrimEnd('\\')})";
                        return;
                    }
                }
            }
        }
        catch { }

        DiskRemainingText = "-- GB";
    }


    private int _telemetryFailures = 0;

    private async void OnTelemetryTimerTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _telemetryPollInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            await QueryTelemetryAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _telemetryPollInFlight, 0);
        }
    }

    internal async Task QueryTelemetryAsync()
    {
        if (!IsRecording && !IsPaused) return;

        try
        {
            var response = await _ipcClient.SendCommandAsync("GetTelemetry", new { }, timeoutMs: 1500);
            if (response.Success && !string.IsNullOrEmpty(response.ErrorMessage))
            {
                var telemetry = JsonSerializer.Deserialize<RecorderTelemetry>(response.ErrorMessage);
                if (telemetry != null)
                {
                    _telemetryFailures = 0;
                    if (telemetry.State == RecordingState.Paused)
                    {
                        IsPaused = true;
                        IsRecording = false;
                    }
                    else if (telemetry.State == RecordingState.Recording)
                    {
                        IsRecording = true;
                        IsPaused = false;
                    }
                    else
                    {
                        // 核心進程已完成或已結束，同步 UI 狀態
                        IsPaused = false;
                        IsRecording = false;
                        _isDiskSpaceWarningActive = false;
                        _telemetryTimer.Stop();
                        return;
                    }

                    ElapsedTimeText = telemetry.ElapsedTime.ToString(@"hh\:mm\:ss");
                    FileSizeText = $"{telemetry.CurrentFileSizeBytes / (1024.0 * 1024.0):F1} MB";
                    DiskRemainingText = $"{telemetry.AvailableDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";

                    if (IsDiskSpaceWarning(
                            telemetry.State,
                            telemetry.AvailableDiskSpaceBytes,
                            _activeDiskWarningThresholdBytes))
                    {
                        _isDiskSpaceWarningActive = true;
                        StatusMessage = Strings.GetFormatted(
                            "StatusDiskSpaceWarning",
                            telemetry.AvailableDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0));
                    }
                    else if (_isDiskSpaceWarningActive)
                    {
                        _isDiskSpaceWarningActive = false;
                        StatusMessage = telemetry.State == RecordingState.Paused
                            ? Strings["StatusPausedMsg"]
                            : Strings["StatusRecordingActive"];
                    }
                    return;
                }
            }

            _telemetryFailures++;
        }
        catch
        {
            _telemetryFailures++;
        }

        // 當連續 6 次 (3 秒) 逾時或中斷，判定錄影核心異常中止
        if (_telemetryFailures >= 6)
        {
            _telemetryTimer.Stop();
            _telemetryFailures = 0;
            IsRecording = false;
            IsPaused = false;
            StatusMessage = Strings["StatusUnexpectedDisconnect"];
            await CheckRecoverableSessionsAsync();
            _ = RecheckRecoverableSessionsAfterHeartbeatTimeoutAsync();
        }
    }

    private async Task RecheckRecoverableSessionsAfterHeartbeatTimeoutAsync()
    {
        await Task.Delay(
            RecordingRecoveryService.ActiveHeartbeatTimeout + TimeSpan.FromSeconds(1));

        if (!IsRecording && !IsPaused && !IsPreparing)
        {
            await CheckRecoverableSessionsAsync();
        }
    }

    private string _currentPipeName = NamedPipeConstants.PipeBaseName;

    private async Task EnsureRecorderProcessAsync()
    {
        var ping = await _ipcClient.SendCommandAsync("Ping", new { }, timeoutMs: 1000);
        if (ping.Success) return;

        // Create a unique pipe name for this session to avoid zombie conflicts
        _currentPipeName = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        _ipcClient = new NamedPipeIpcClient(_currentPipeName);

        var recorderExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(recorderExe) || !File.Exists(recorderExe))
        {
            throw new Exception("無法取得主程式路徑。");
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = recorderExe,
            Arguments = $"--daemon --pipe {_currentPipeName} --parent-pid {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _recorderProcess = System.Diagnostics.Process.Start(psi);
        
        // Poll to wait for extraction and startup (up to 10 seconds)
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            var retryPing = await _ipcClient.SendCommandAsync("Ping", new { }, timeoutMs: 500);
            if (retryPing.Success) return;
        }

        try { _recorderProcess?.Kill(true); } catch { }
        throw new Exception("啟動背景引擎超時，請檢查防毒軟體或重試。");
    }

    public void Cleanup()
    {
        _telemetryTimer.Stop();
        _telemetryTimer.Tick -= OnTelemetryTimerTick;

        try
        {
            _globalHotkeyService?.Dispose();
        }
        catch { }

        try
        {
            if (_ipcClient != null)
            {
                // 發送 Shutdown 通知 Recorder 正常結束常駐
                _ = _ipcClient.SendCommandAsync("Shutdown", new { }, timeoutMs: 800);
            }
        }
        catch { }

        try
        {
            if (_recorderProcess != null && !_recorderProcess.HasExited)
            {
                if (!_recorderProcess.WaitForExit(1000))
                {
                    _recorderProcess.Kill(true);
                }
            }
        }
        catch { }
    }
}
