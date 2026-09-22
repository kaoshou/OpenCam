// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Enums;

/// <summary>
/// 錄影生命週期狀態
/// </summary>
public enum RecordingState
{
    /// <summary>
    /// 閒置待命
    /// </summary>
    Idle,

    /// <summary>
    /// 準備與初始化影音管線中
    /// </summary>
    Preparing,

    /// <summary>
    /// 正在錄影
    /// </summary>
    Recording,

    /// <summary>
    /// 正在暫停中
    /// </summary>
    Pausing,

    /// <summary>
    /// 已暫停
    /// </summary>
    Paused,

    /// <summary>
    /// 正在停止錄影並 Flush 殘餘數據
    /// </summary>
    Stopping,

    /// <summary>
    /// MKV 工作檔已安全封裝，正在執行 Remux 為 MP4
    /// </summary>
    Finalizing,

    /// <summary>
    /// 錄影與 Remux 驗證均正常完成
    /// </summary>
    Completed,

    /// <summary>
    /// 遭遇非預期例外或外力中斷
    /// </summary>
    Interrupted,

    /// <summary>
    /// 偵測到可救援的未正常結束工作階段
    /// </summary>
    Recoverable,

    /// <summary>
    /// 重大故障
    /// </summary>
    Failed
}

/// <summary>
/// 畫面擷取來源類型
/// </summary>
public enum CaptureSourceType
{
    FullScreen,
    Monitor,
    CustomRegion
}

/// <summary>
/// 音訊來源配置
/// </summary>
public enum AudioSourceType
{
    None,
    SystemOnly,
    MicrophoneOnly,
    SystemAndMicrophone
}
