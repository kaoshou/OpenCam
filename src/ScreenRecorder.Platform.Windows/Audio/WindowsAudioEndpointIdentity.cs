// SPDX-License-Identifier: AGPL-3.0-or-later
using NAudio.CoreAudioApi;

namespace ScreenRecorder.Platform.Windows.Audio;

/// <summary>Read-only endpoint identity; never starts a WASAPI capture.</summary>
public static class WindowsAudioEndpointIdentity
{
    public static string Read(bool microphone, bool system)
    {
        using var devices = new MMDeviceEnumerator();
        var ids = new List<string>();
        if (microphone)
        {
            using var input = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            ids.Add("default:" + input.ID);
            foreach (var endpoint in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                using (endpoint) ids.Add("input:" + endpoint.ID);
        }
        if (system)
        {
            using var output = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            ids.Add("output:" + output.ID);
        }
        return string.Join("\n", ids.Order(StringComparer.Ordinal));
    }
}
