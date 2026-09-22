// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public record AudioDeviceOption(string Id, string Name, bool IsDefault, bool IsInput)
{
    public override string ToString() => Name;
}
