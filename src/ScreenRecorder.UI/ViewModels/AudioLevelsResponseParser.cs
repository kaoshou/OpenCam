// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public static class AudioLevelsResponseParser
{
    public static bool TryParse(string? payload, out AudioLevelsSnapshot snapshot)
    {
        snapshot = default;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("SystemAudio", out var system) ||
                !document.RootElement.TryGetProperty("Microphone", out var microphone) ||
                !HasFields(system) || !HasFields(microphone))
            {
                return false;
            }

            snapshot = JsonSerializer.Deserialize<AudioLevelsSnapshot>(payload);
            return Valid(snapshot.SystemAudio) && Valid(snapshot.Microphone);
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    private static bool HasFields(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("State", out _) &&
        element.TryGetProperty("Rms", out _) &&
        element.TryGetProperty("Peak", out _);

    private static bool Valid(AudioSourceLevel level) =>
        Enum.IsDefined(level.State) &&
        double.IsFinite(level.Rms) && level.Rms is >= 0 and <= 1 &&
        double.IsFinite(level.Peak) && level.Peak is >= 0 and <= 1;
}
