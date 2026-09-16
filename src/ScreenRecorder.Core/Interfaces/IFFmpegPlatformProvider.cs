using System.Collections.Generic;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

/// <summary>
/// 提供 FFmpeg 在不同作業系統（Windows, macOS, Linux）下的專屬擷取與編碼參數
/// </summary>
public interface IFFmpegPlatformProvider
{
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
}
