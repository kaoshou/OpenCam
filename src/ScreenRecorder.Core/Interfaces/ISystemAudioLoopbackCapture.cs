// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenRecorder.Core.Interfaces;

/// <summary>
/// 系統音訊擷取輸出資訊 (包含本機音訊管道與 PCM 取樣格式)
/// </summary>
public record SystemAudioCaptureInfo(string PipePath, int SampleRate, int Channels, string FfmpegInputArgs)
{
    public Stream? PcmStream { get; init; }
}

/// <summary>
/// 跨平台系統聲音擷取抽象介面 (Windows 下由 WASAPI Loopback 實作)
/// </summary>
public interface ISystemAudioLoopbackCapture : IAsyncDisposable
{
    bool IsSupported { get; }
    bool IsCapturing { get; }

    /// <summary>
    /// 開始系統音訊擷取，並建立供 FFmpeg 即時讀取的 Named Pipe
    /// </summary>
    Task<SystemAudioCaptureInfo?> StartCaptureAsync(
        int monitorIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止系統音訊擷取並安全釋放資源
    /// </summary>
    Task StopCaptureAsync(CancellationToken cancellationToken = default);

    event EventHandler<string>? AudioErrorOccurred;
}
