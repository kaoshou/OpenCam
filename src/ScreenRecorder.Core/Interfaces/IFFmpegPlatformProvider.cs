// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

/// <summary>
/// 提供 FFmpeg 在不同作業系統（Windows, macOS, Linux）下的專屬擷取與編碼參數
/// </summary>
public interface IFFmpegPlatformProvider
{
    // Full live-check command tail: native capture -> scaled raw RGBA.
    // videoInput is the already validated/pinned Windows capture plan, when present.
    string BuildDirectPreviewArguments(RecordingConfiguration config,
        int x, int y, int width, int height, bool synthetic, bool hasDirectShowMic,
        string? systemAudioPipeArg, string? microphoneAudioPipeArg, string? videoInput = null) =>
        throw new NotSupportedException("Direct preview is unavailable for this capture provider.");

    /// <summary>
    /// 取得硬體加速編碼器的快速預檢清單
    /// </summary>
    IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes();

    /// <summary>
    /// 取得該平台專屬的視訊與音訊 FFmpeg 完整輸入參數組合
    /// </summary>
    string BuildInputArguments(
        RecordingConfiguration config,
        int x, int y, int width, int height,
        bool useSynthetic,
        bool hasDirectShowMic,
        string? systemAudioPipeArg = null,
        string? microphoneAudioPipeArg = null); // 若不依賴 DirectShow，這裡應改為 hasMicDevice，我們維持現有參數名以求平滑轉移

    /// <summary>
    /// 取得該平台特定硬體編碼器的輸出與影片編碼參數
    /// </summary>
    string BuildOutputArguments(
        RecordingConfiguration config,
        HardwareEncoderType encoderType,
        string workingFilePath);

    // Explicitly opt in for disposable live preview only. Providers without a
    // specialized profile retain their normal encoder and output behavior.
    string BuildLivePreviewOutputArguments(RecordingConfiguration config,
        HardwareEncoderType encoderType, string outputPath) =>
        BuildOutputArguments(config, encoderType, outputPath);
}
