// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenRecorder.Platform.Linux;

// Private Recorder-side preparation result, deliberately not a record/DTO.
// Never send this object through ordinary IPC, settings or project persistence.
internal sealed class LinuxPortalSelection
{
    internal string SourceId { get; }
    internal string RestoreToken { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int PreviewWidth { get; }
    internal int PreviewHeight { get; }
    internal byte[] Rgba { get; }
    private LinuxPortalSelection(string sourceId, string token, int width, int height, int previewWidth, int previewHeight, byte[] rgba)
        => (SourceId, RestoreToken, Width, Height, PreviewWidth, PreviewHeight, Rgba) =
           (sourceId, token, width, height, previewWidth, previewHeight, rgba);
    public override string ToString() => "Linux portal selection (private credentials redacted)";

    internal static LinuxPortalSelection Parse(string privateEvents, byte[] rgba)
    {
        try {
            if (privateEvents.Length > 256 * 1024 || rgba.Length > 480 * 270 * 4) throw new InvalidDataException();
            LinuxPortalSelection? selection = null;
            foreach (var line in privateEvents.Split('\n')) {
                if (!line.StartsWith("OCPORTAL ", StringComparison.Ordinal)) continue;
                if (line.Length > 65536) throw new InvalidDataException();
                using var doc = JsonDocument.Parse(line[9..]);
                var e = doc.RootElement;
                var name = e.GetProperty("event").GetString();
                if (name == "error" || name == "environment_unavailable") throw new InvalidDataException();
                if (name != "selected") continue;
                if (selection is not null || e.GetProperty("backend").GetString() != "pipewire") throw new InvalidDataException();
                var source = e.GetProperty("sourceId").GetString() ?? "";
                var token = e.GetProperty("restoreToken").GetString() ?? "";
                var width = e.GetProperty("width").GetInt32(); var height = e.GetProperty("height").GetInt32();
                var pw = e.GetProperty("previewWidth").GetInt32(); var ph = e.GetProperty("previewHeight").GetInt32();
                if (!Regex.IsMatch(source, @"\Aportal:[a-f0-9]{64}\z") || token.Length == 0 ||
                    Encoding.UTF8.GetByteCount(token) > 4096 || token.Any(c => c < 32 || c == 127) ||
                    width is < 2 or > 16384 || height is < 2 or > 16384 || pw is < 1 or > 480 || ph is < 1 or > 270 ||
                    rgba.Length != pw * ph * 4) throw new InvalidDataException();
                var scale = Math.Min(1.0, Math.Min(480.0 / width, 270.0 / height));
                if (pw != Math.Max(1, (int)(width * scale)) || ph != Math.Max(1, (int)(height * scale)))
                    throw new InvalidDataException();
                var drawn = false;
                for (var i = 3; i < rgba.Length; i += 4) drawn |= rgba[i] != 0;
                if (!drawn) throw new InvalidDataException();
                selection = new(source, token, width, height, pw, ph, (byte[])rgba.Clone());
            }
            return selection ?? throw new InvalidDataException();
        } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or InvalidDataException) {
            // JsonException includes portions of input. Do not retain it as an
            // InnerException: malformed private payloads can contain a token.
            throw new IOException("Linux source selection was cancelled, unavailable or invalid; select the recording source again.");
        }
    }
}
