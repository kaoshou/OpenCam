using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenRecorder.Core.Interfaces;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Audio;

/// <summary>
/// 使用 Windows 原生 WASAPI Loopback 擷取系統正在播放之聲音 (YouTube、應用程式、遊戲聲音)
/// 並透過本機 Named Pipe 將 PCM 音訊流即時饋送予 FFmpeg。
/// 內建 WasapiOut 靜音時鐘保持機制，徹底根絕錄音斷斷續續與音訊漂移問題。
/// </summary>
public class WindowsWasapiLoopbackCapture : ISystemAudioLoopbackCapture
{
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _silencePlayer;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private readonly object _syncLock = new();
    private DateTime _lastWriteTimeUtc = DateTime.UtcNow;
    private byte[] _conversionBuffer = new byte[65536];
    private bool _isDisposed;
    private bool _isCapturing;

    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsCapturing => _isCapturing;

    public event EventHandler<string>? AudioErrorOccurred;

    public async Task<SystemAudioCaptureInfo?> StartCaptureAsync(
        int monitorIndex,
        CancellationToken cancellationToken = default)
    {
        _ = monitorIndex;
        if (!IsSupported) return null;

        lock (_syncLock)
        {
            if (_isCapturing)
            {
                throw new InvalidOperationException("WASAPI Loopback 系統聲音擷取已在運作中");
            }
        }

        try
        {
            _cts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            // 1. 初始化 WASAPI Loopback Capture
            _capture = new WasapiLoopbackCapture();
            var waveFormat = _capture.WaveFormat;
            int sampleRate = waveFormat.SampleRate;
            int channels = waveFormat.Channels;
            bool isFloat = waveFormat.Encoding == WaveFormatEncoding.IeeeFloat;

            Log.Information("初始化 WASAPI Loopback 擷取成功: {SampleRate} Hz, {Channels} 聲道, 編碼: {Encoding}",
                sampleRate, channels, waveFormat.Encoding);

            // 2. 啟動 WasapiOut 靜音時鐘保持器 (Silence Keep-Alive Player)
            // 只要系統上有一個播放客戶端處於播放狀態，Windows Audio Engine 就會持續以硬體時脈觸發 Loopback 回調，
            // 徹底解決系統未播放聲音時回調停擺、以及心跳幫浦插隊導致音樂斷斷續續的問題！
            try
            {
                var silenceProvider = new SilenceWaveProvider(waveFormat);
                _silencePlayer = new WasapiOut(AudioClientShareMode.Shared, 200);
                _silencePlayer.Init(silenceProvider);
                _silencePlayer.Play();
                Log.Information("WASAPI 靜音時鐘保持器 (Silence Keep-Alive) 啟動成功");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "無法啟動 WASAPI 靜音時鐘保持器，將依賴備援心跳補償機制");
            }

            // 3. 建立供 FFmpeg 即時讀取之本機 Named Pipe
            var pipeName = $"OpenCam_SysAudio_{Guid.NewGuid():N}";
            var pipePath = $@"\\.\pipe\{pipeName}";

            _pipeServer = new NamedPipeServerStream(
                pipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                65536,
                65536);

            // 背景等待 FFmpeg 連線
            _ = Task.Run(async () =>
            {
                try
                {
                    await _pipeServer.WaitForConnectionAsync(linkedCts.Token);
                    Log.Information("FFmpeg 已成功連線至 WASAPI Loopback 音訊管道: {Pipe}", pipeName);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Warning(ex, "音訊管道等待連線時發生異常: {Pipe}", pipeName);
                }
            }, linkedCts.Token);

            _lastWriteTimeUtc = DateTime.UtcNow;

            // 4. 綁定音訊資料回調 (將 IEEE Float 轉換為標準 16-bit PCM s16le 以獲取極致相容性與無損音質)
            _capture.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded <= 0 || _pipeServer == null || !_pipeServer.IsConnected) return;

                try
                {
                    lock (_syncLock)
                    {
                        if (_pipeServer == null || !_pipeServer.IsConnected) return;

                        if (isFloat)
                        {
                            // 32-bit Float -> 16-bit signed integer (s16le)
                            int sampleCount = e.BytesRecorded / 4;
                            int requiredBytes = sampleCount * 2;
                            if (_conversionBuffer.Length < requiredBytes)
                            {
                                _conversionBuffer = new byte[requiredBytes * 2];
                            }

                            for (int i = 0; i < sampleCount; i++)
                            {
                                float floatSample = BitConverter.ToSingle(e.Buffer, i * 4);
                                short shortSample = (short)Math.Clamp((int)(floatSample * 32767f), -32768, 32767);
                                _conversionBuffer[i * 2] = (byte)(shortSample & 0xFF);
                                _conversionBuffer[i * 2 + 1] = (byte)((shortSample >> 8) & 0xFF);
                            }

                            _pipeServer.Write(_conversionBuffer, 0, requiredBytes);
                        }
                        else
                        {
                            _pipeServer.Write(e.Buffer, 0, e.BytesRecorded);
                        }

                        _lastWriteTimeUtc = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "寫入音訊管道時發生非致命例外 (可能管線正常關閉)");
                }
            };

            _capture.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null)
                {
                    Log.Error(e.Exception, "WASAPI Loopback 錄音非正常中止");
                    AudioErrorOccurred?.Invoke(this, e.Exception.Message);
                }
            };

            // 5. 啟動 WASAPI 錄音
            _capture.StartRecording();
            _isCapturing = true;

            // 6. 啟動備援靜音時鐘補償 (僅在 Silence Keep-Alive 失效且長達 350ms 無任何回調時作為安全網)
            _heartbeatTask = Task.Run(() => BackupSilenceHeartbeatLoopAsync(sampleRate, channels, linkedCts.Token), linkedCts.Token);

            // 組裝 FFmpeg 輸入參數 (使用標準 s16le PCM 格式)
            var ffmpegInputArgs = $"-f s16le -ar {sampleRate} -ac {channels} -i \"{pipePath}\" ";

            return new SystemAudioCaptureInfo(pipePath, sampleRate, channels, ffmpegInputArgs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "啟動 WASAPI Loopback 擷取失敗");
            await CleanupAsync();
            AudioErrorOccurred?.Invoke(this, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 備援靜音補償循環：僅在系統長達 350ms 以上完全無任何 WASAPI 回調時啟動，
    /// 絕不干擾正在播放的正常音樂與音訊串流。
    /// </summary>
    private async Task BackupSilenceHeartbeatLoopAsync(int sampleRate, int channels, CancellationToken ct)
    {
        int checkIntervalMs = 100;
        int silenceBytesPerCheck = (int)(sampleRate * channels * 2 * (checkIntervalMs / 1000.0));
        byte[] silenceBuffer = new byte[silenceBytesPerCheck];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(checkIntervalMs, ct);

                if (_pipeServer == null || !_pipeServer.IsConnected) continue;

                var elapsedMs = (DateTime.UtcNow - _lastWriteTimeUtc).TotalMilliseconds;
                // 門檻提升至 350ms：確保正常播放中的短暫抖動不會誤觸，僅在系統真正寂靜停擺時補齊時脈
                if (elapsedMs >= 350)
                {
                    lock (_syncLock)
                    {
                        if (_pipeServer != null && _pipeServer.IsConnected)
                        {
                            _pipeServer.Write(silenceBuffer, 0, silenceBuffer.Length);
                            _lastWriteTimeUtc = DateTime.UtcNow;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "備援靜音幫浦循環發生非致命異常");
        }
    }

    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;
        }

        await CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }

        if (_heartbeatTask != null)
        {
            try { await _heartbeatTask; } catch { }
            _heartbeatTask = null;
        }

        try
        {
            _silencePlayer?.Stop();
            _silencePlayer?.Dispose();
            _silencePlayer = null;
        }
        catch { }

        try
        {
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
        }
        catch { }

        lock (_syncLock)
        {
            try
            {
                if (_pipeServer != null)
                {
                    if (_pipeServer.IsConnected)
                    {
                        try { _pipeServer.Flush(); } catch { }
                    }
                    _pipeServer.Dispose();
                    _pipeServer = null;
                }
            }
            catch { }
        }

        _isCapturing = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopCaptureAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 自訂極簡靜音音訊源，提供 WasapiOut 進行背景時鐘保持
    /// </summary>
    private sealed class SilenceWaveProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; }

        public SilenceWaveProvider(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
