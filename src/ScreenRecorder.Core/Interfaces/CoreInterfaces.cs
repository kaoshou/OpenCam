using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

public record AudioDeviceInfo(string Id, string Name, bool IsDefault, bool IsInput);

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceOption> GetRecordingDevices();
    IReadOnlyList<AudioDeviceOption> GetPlaybackDevices();
}

public record MonitorInfo(int Index, string DeviceName, CaptureRegion Bounds, bool IsPrimary, double DpiScaling);

public interface IDisplayService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    MonitorInfo? GetPrimaryMonitor();
    CaptureRegion GetVirtualScreenBounds();
}

public interface IRecordingSessionStore
{
    Task SaveSessionAsync(RecordingSession session, CancellationToken cancellationToken = default);
    Task<RecordingSession?> LoadSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecordingSession>> FindAllSessionsAsync(string rootRecordingsPath, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default);
}

public interface IStorageService
{
    string GetDefaultRecordingsPath();
    string CreateSessionDirectory(string rootPath, string sessionId);
    string GetWorkingFilePath(string sessionDirectory);
    string GetFinalFilePath(string rootPath, DateTimeOffset timestamp);
    string GetSessionLogFilePath(string sessionDirectory);
    long GetAvailableFreeSpaceBytes(string directoryPath);
    bool IsDiskSpaceSufficient(string directoryPath, long requiredBytes);
}

public class VideoFrameData
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public long TimestampNanoseconds { get; }

    public VideoFrameData(byte[] data, int width, int height, long timestampNanoseconds)
    {
        Data = data;
        Width = width;
        Height = height;
        TimestampNanoseconds = timestampNanoseconds;
    }
}

public interface IVideoCaptureService : IAsyncDisposable
{
    bool IsCapturing { get; }
    int Width { get; }
    int Height { get; }
    Task StartCaptureAsync(RecordingConfiguration config, CancellationToken cancellationToken);
    Task StopCaptureAsync(CancellationToken cancellationToken);
    event EventHandler<VideoFrameData>? FrameArrived;
    event EventHandler<string>? CaptureErrorOccurred;
}

public class AudioSamplesData
{
    public byte[] Data { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TimestampNanoseconds { get; }

    public AudioSamplesData(byte[] data, int sampleRate, int channels, long timestampNanoseconds)
    {
        Data = data;
        SampleRate = sampleRate;
        Channels = channels;
        TimestampNanoseconds = timestampNanoseconds;
    }
}

public interface IAudioCaptureService : IAsyncDisposable
{
    bool IsCapturing { get; }
    Task StartCaptureAsync(RecordingConfiguration config, CancellationToken cancellationToken);
    Task StopCaptureAsync(CancellationToken cancellationToken);
    event EventHandler<AudioSamplesData>? SamplesArrived;
    event EventHandler<string>? AudioErrorOccurred;
}

public interface IEncoder : IAsyncDisposable
{
    bool IsRunning { get; }
    Task InitializeAsync(string outputFilePath, RecordingConfiguration config, int width, int height, CancellationToken cancellationToken);
    Task SendVideoFrameAsync(VideoFrameData frame, CancellationToken cancellationToken);
    Task SendAudioSamplesAsync(AudioSamplesData samples, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}

public interface IStreamCopyRemuxer
{
    Task<bool> RemuxToMp4Async(string mkvInputPath, string mp4OutputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> ConcatAndRemuxToMp4Async(IReadOnlyList<string> mkvInputPaths, string mp4OutputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public record MediaProbeResult(
    bool IsValid,
    string FormatName,
    TimeSpan Duration,
    long FileSizeBytes,
    int VideoStreamCount,
    int AudioStreamCount,
    string? VideoCodec,
    int Width,
    int Height,
    double Fps,
    string? AudioCodec,
    int SampleRate,
    string? RawJsonOutput
);

public interface IMediaProbeService
{
    Task<MediaProbeResult> ProbeAsync(string mediaFilePath, CancellationToken cancellationToken = default);
}

public interface IDiskSpaceMonitor : IDisposable
{
    long WarningThresholdBytes { get; set; }
    long CriticalThresholdBytes { get; set; }
    void StartMonitoring(string targetDirectoryPath, TimeSpan interval);
    void StopMonitoring();
    event EventHandler<long>? DiskSpaceWarningTriggered;
    event EventHandler<long>? DiskSpaceCriticalTriggered;
}

public record RecoverableSessionInfo(
    RecordingSession Session,
    long WorkingFileSizeBytes,
    bool HasWorkingFile,
    string StatusDescription
);

public interface IRecordingRecoveryService
{
    Task<IReadOnlyList<RecoverableSessionInfo>> ScanForRecoverableSessionsAsync(string rootRecordingsPath, CancellationToken cancellationToken = default);
    Task<(bool Success, bool IsPartial, string? ErrorMessage, string? FinalMp4Path)> RecoverSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default);
}

public interface IRecorderHealthMonitor
{
    void RecordFrameReceived();
    void RecordAudioReceived();
    void RecordEncoderActivity();
    RecorderTelemetry GetCurrentTelemetry(string sessionId, TimeSpan elapsed, string workingFile, string finalFile);
}

public interface ISettingsService
{
    Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
}

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    event EventHandler<int>? HotkeyTriggered;
    bool RegisterHotkey(int virtualKey, uint modifiers = 0);
    bool RegisterHotkey(int id, int virtualKey, uint modifiers = 0);
    void UnregisterHotkey();
    void UnregisterHotkey(int id);
}

public interface IEncoderDetector
{
    Task<IReadOnlyList<EncoderCapability>> DetectAvailableEncodersAsync(CancellationToken cancellationToken = default);
    Task<HardwareEncoderType> ResolveOptimalEncoderAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default);
}

public interface IDisplayChangeMonitor : IDisposable
{
    event EventHandler? DisplayChanged;
    void Start();
    void Stop();
}

public interface ICursorHighlightService : IDisposable
{
    bool IsRunning { get; }
    void Start(CursorEffectMode mode);
    void Stop();
}


