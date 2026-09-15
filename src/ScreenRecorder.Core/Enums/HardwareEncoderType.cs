namespace ScreenRecorder.Core.Enums;

/// <summary>
/// 硬體編碼器類型
/// </summary>
public enum HardwareEncoderType
{
    /// <summary>
    /// 自動偵測並優先選擇系統最佳硬體加速器
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 軟體 CPU 編碼 (x264)
    /// </summary>
    SoftwareCpu = 1,

    /// <summary>
    /// NVIDIA NVENC H.264
    /// </summary>
    NvidiaNvenc = 2,

    /// <summary>
    /// Intel Quick Sync Video H.264
    /// </summary>
    IntelQsv = 3,

    /// <summary>
    /// AMD AMF H.264
    /// </summary>
    AmdAmf = 4,

    /// <summary>
    /// Apple VideoToolbox H.264 (macOS)
    /// </summary>
    AppleVideoToolbox = 5
}

/// <summary>
/// 編碼效能預設選項
/// </summary>
public enum EncoderPreset
{
    Fast,
    Medium,
    Slow
}

/// <summary>
/// 編碼器能力與狀態描述
/// </summary>
public record EncoderCapability(
    HardwareEncoderType Type,
    string CodecName,
    string DisplayName,
    bool IsAvailable
);