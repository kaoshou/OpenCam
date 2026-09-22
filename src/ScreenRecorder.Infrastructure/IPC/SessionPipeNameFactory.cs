// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Infrastructure.IPC;

public static class SessionPipeNameFactory
{
    public static string Create(bool isWindows)
    {
        var token = Guid.NewGuid().ToString("N");
        return isWindows
            ? NamedPipeConstants.PipeBaseName + "_" + token
            : "oc_" + token[..16];
    }
}
