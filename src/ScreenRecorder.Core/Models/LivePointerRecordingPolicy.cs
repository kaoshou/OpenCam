// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public static class LivePointerRecordingPolicy
{
    /// <summary>Keep deserialization compatibility, but never create new editable tracks.</summary>
    public static bool Apply(RecordingConfiguration configuration)
    {
        var changed = configuration.EditablePointerEnabled;
        configuration.EditablePointerEnabled = false;
        return changed;
    }
}
