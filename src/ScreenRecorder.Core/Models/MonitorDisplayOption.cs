namespace ScreenRecorder.Core.Models;

public record MonitorDisplayOption(int Index, string DisplayName, int Width, int Height, bool IsPrimary)
{
    public override string ToString() => $"{DisplayName} ({Width}x{Height}){(IsPrimary ? " [主螢幕]" : "")}";
}
