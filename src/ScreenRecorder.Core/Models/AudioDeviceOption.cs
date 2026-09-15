namespace ScreenRecorder.Core.Models;

public record AudioDeviceOption(string Id, string Name, bool IsDefault, bool IsInput)
{
    public override string ToString() => Name;
}
