namespace ScreenRecorder.Core.Enums;

/// <summary>
/// 錄影時滑鼠游標之呈現效果模式
/// </summary>
public enum CursorEffectMode
{
    /// <summary>
    /// 原始系統游標 (標準呈現)
    /// </summary>
    Default,

    /// <summary>
    /// 醒目黃色光圈 (數位教學推薦)
    /// </summary>
    HighlightHalo,

    /// <summary>
    /// 醒目光圈 + 點擊波紋動畫 (專業教學演示)
    /// </summary>
    HaloWithClickRipple,

    /// <summary>
    /// 隱藏滑鼠游標 (完全不錄製游標)
    /// </summary>
    Hidden
}
