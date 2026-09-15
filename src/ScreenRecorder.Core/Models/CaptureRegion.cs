namespace ScreenRecorder.Core.Models;

/// <summary>
/// 螢幕自訂錄影區域（物理像素座標）
/// </summary>
public record CaptureRegion(int X, int Y, int Width, int Height)
{
    public static CaptureRegion Empty => new(0, 0, 0, 0);

    public bool IsValid => Width > 0 && Height > 0;
}
