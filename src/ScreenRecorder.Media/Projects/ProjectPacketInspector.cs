// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Constant packet memory. All input is from a closed probe job over an already bound handle.</summary>
public static class ProjectPacketInspector
{
    public static async Task<ProjectSourceEvidence> InspectAsync(IProjectStreamingMediaProcess process, FileStream source,
        CancellationToken ct, Action<int, long, long>? observe = null)
    {
        ProjectSourceEvidence? evidence = null;
        await process.RunStreamingAsync(ProjectMediaJob.InspectPackets(source.Length), source,
            async (stream, token) => { evidence = await ParseAsync(stream, token, observe); }, ct);
        return evidence ?? throw new InvalidDataException("Missing source evidence.");
    }

    public static async Task<ProjectSourceEvidence> ParseAsync(Stream stream, CancellationToken ct, Action<int, long, long>? observe = null)
    {
        var packets = new Dictionary<int, PacketSummary>();
        var streams = new Dictionary<int, Dictionary<string, string>>();
        double formatStart = 0;
        var line = new StringBuilder();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        var buffer = new char[4096];
        int count;
        try
        {
            while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
                foreach (var character in buffer.AsSpan(0, count).ToArray())
                {
                    if (character == '\n') { ReadLine(line.ToString()); line.Clear(); }
                    else if (character != '\r')
                    {
                        if (line.Length >= 65536) throw new InvalidDataException("Probe line exceeded limit.");
                        line.Append(character);
                    }
                }
            if (line.Length > 0) ReadLine(line.ToString());
            var videos = streams.Where(p => p.Value.GetValueOrDefault("codec_type") == "video").ToArray();
            var audios = streams.Where(p => p.Value.GetValueOrDefault("codec_type") == "audio").ToArray();
            if (videos.Length != 1 || audios.Length > 1 || streams.Count != videos.Length + audios.Length)
                throw new InvalidDataException("Expected one video and at most one audio stream.");
            return new(Build(videos[0]), audios.Length == 0 ? null : Build(audios[0])) { FormatStartSeconds = formatStart };
        }
        catch (OverflowException ex) { throw new InvalidDataException("Probe timestamp overflow.", ex); }

        void ReadLine(string text)
        {
            if (text.Length == 0) return;
            var parts = text.Split('|');
            if (parts[0] is not ("packet" or "stream" or "format")) return;
            var values = new Dictionary<string, string>();
            foreach (var field in parts.Skip(1))
            {
                var pair = field.Split('=', 2);
                if (pair.Length == 2) values[pair[0]] = pair[1];
            }
            if (parts[0] == "format")
            {
                if (!double.TryParse(values.GetValueOrDefault("start_time"), NumberStyles.Float, CultureInfo.InvariantCulture, out formatStart) ||
                    !double.IsFinite(formatStart)) throw new InvalidDataException("Missing format start time.");
                return;
            }
            var index = checked((int)Integer(values, parts[0] == "stream" ? "index" : "stream_index"));
            if (index is < 0 or > 15) throw new InvalidDataException("Unexpected stream index.");
            if (parts[0] == "stream") { streams.Add(index, values); return; }
            if (!packets.TryGetValue(index, out var summary)) packets.Add(index, summary = new());
            var pts = Integer(values, "pts");
            // Packet duration is optional probe evidence, not proof that the source
            // is corrupt. Without it we cannot certify stream-copy boundaries.
            if (!values.TryGetValue("duration", out var durationText) ||
                !long.TryParse(durationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
                throw new ProjectRemuxIncompatibleException("Packet duration is unavailable; verified rendering is required.");
            if (summary.Count == 0) summary.Key = values.GetValueOrDefault("flags", "").Contains('K');
            summary.First = Math.Min(summary.First, pts);
            summary.End = Math.Max(summary.End, checked(pts + duration));
            summary.Duration = Math.Max(summary.Duration, duration);
            summary.Count = checked(summary.Count + 1);
            observe?.Invoke(index, pts, duration);
            if (long.TryParse(values.GetValueOrDefault("dts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dts))
            {
                if (summary.Dts is { } previous && dts <= previous) summary.Monotonic = false;
                summary.Dts = dts;
            }
        }

        ProjectStreamEvidence Build(KeyValuePair<int, Dictionary<string, string>> entry)
        {
            var fields = entry.Value;
            if (!packets.TryGetValue(entry.Key, out var p) || p.Count == 0) throw new InvalidDataException("Empty media stream.");
            var time = fields.GetValueOrDefault("time_base", "").Split('/');
            if (time.Length != 2 || !long.TryParse(time[0], out var n) || !long.TryParse(time[1], out var d) ||
                n <= 0 || d <= 0 || n > int.MaxValue || d > int.MaxValue) throw new InvalidDataException("Invalid time base.");
            return new(fields.GetValueOrDefault("codec_name", ""), fields.GetValueOrDefault("extradata_hash", ""),
                OptionalInt("width"), OptionalInt("height"), fields.GetValueOrDefault("pix_fmt", ""), new(n, d),
                fields.GetValueOrDefault("r_frame_rate", ""), OptionalInt("sample_rate"), Layout(),
                p.Count, p.First, p.End, p.Duration, p.Key, p.Monotonic);
            int OptionalInt(string name) => fields.TryGetValue(name, out var value) && int.TryParse(value, out var parsed) ? parsed : 0;
            string Layout() => fields.GetValueOrDefault("channel_layout", "") is "" or "unknown"
                ? OptionalInt("channels") switch { 1 => "mono", 2 => "stereo", _ => "unknown" }
                : fields["channel_layout"];
        }
    }

    private static long Integer(Dictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out var text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidDataException($"Missing packet {name}.");
    private sealed class PacketSummary
    {
        public long Count, Duration;
        public long First = long.MaxValue, End = long.MinValue;
        public long? Dts;
        public bool Key, Monotonic = true;
    }
}
