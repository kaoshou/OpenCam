// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows.Audio;

public static class WindowsMicrophoneEndpointMatcher
{
    public static string? Match(string dshowDeviceId,
        IReadOnlyList<AudioDeviceOption> dshowOptions,
        IReadOnlyList<(string Id, string FriendlyName)> endpoints)
    {
        if (string.IsNullOrWhiteSpace(dshowDeviceId) || dshowDeviceId == "default_mic")
        {
            return null;
        }

        var options = dshowOptions.Where(option => option.IsInput &&
            (string.Equals(option.Id, dshowDeviceId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option.Name, dshowDeviceId, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (options.Length != 1)
        {
            return null;
        }

        var matched = endpoints.Where(endpoint =>
            string.Equals(endpoint.FriendlyName.Trim(), options[0].Name.Trim(),
                StringComparison.OrdinalIgnoreCase)).ToArray();
        return matched.Length == 1 ? matched[0].Id : null;
    }
}
