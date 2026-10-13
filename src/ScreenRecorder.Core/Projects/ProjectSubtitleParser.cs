// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
namespace ScreenRecorder.Core.Projects;

public static class ProjectSubtitleParser
{
    private static readonly Regex Time = new(@"^(?:(\d{2,}):)?(\d{2}):(\d{2})[.,](\d{3})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static ImmutableArray<ProjectSubtitleCue> Parse(ReadOnlyMemory<byte> bytes, string extension)
    {
        if (bytes.Length > 10 * 1024 * 1024 || bytes.IsEmpty)
            throw new InvalidDataException("Subtitle must be nonempty and at most 10 MiB.");
        if (!extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only SRT and VTT subtitles are supported.");
        string text;
        var span = bytes.Span;
        try
        {
            if (span.StartsWith(new byte[] { 0xff, 0xfe })) text = new UnicodeEncoding(false, true, true).GetString(span[2..]);
            else if (span.StartsWith(new byte[] { 0xfe, 0xff })) text = new UnicodeEncoding(true, true, true).GetString(span[2..]);
            else
            {
                if (span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) span = span[3..];
                text = new UTF8Encoding(false, true).GetString(span);
            }
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Save subtitles as UTF-8 or BOM-marked UTF-16.", ex); }
        if (text.Contains('\0')) throw new InvalidDataException("Subtitle contains NUL characters.");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        var cues = ImmutableArray.CreateBuilder<ProjectSubtitleCue>();
        var index = 0;
        if (lines[0].StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            index++;
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) index++;
        }
        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
            if (index >= lines.Length) break;
            var first = lines[index];
            if (first == "NOTE" || first.StartsWith("NOTE ", StringComparison.Ordinal) || first is "STYLE" or "REGION")
            {
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) index++;
                continue;
            }
            if (!lines[index].Contains("-->")) index++; // Optional SRT number / VTT cue identifier.
            if (index >= lines.Length || !lines[index].Contains("-->"))
                throw new InvalidDataException("Subtitle cue has no valid timing line.");
            var times = lines[index++].Split("-->");
            if (times.Length != 2) throw new InvalidDataException("Subtitle timing is invalid.");
            var start = ParseTime(times[0].Trim());
            var endPart = times[1].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (endPart.Length == 0) throw new InvalidDataException("Subtitle end time is missing.");
            var end = ParseTime(endPart[0]);
            if (end <= start) throw new InvalidDataException("Subtitle end must be later than start.");
            var cueText = new StringBuilder();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                if (cueText.Length != 0) cueText.Append('\n');
                cueText.Append(lines[index++]);
                if (cueText.Length > 10000) throw new InvalidDataException("Subtitle cue exceeds 10000 characters.");
            }
            if (cueText.Length == 0) throw new InvalidDataException("Subtitle cue text is empty.");
            if (cues.Count >= 100000) throw new InvalidDataException("Subtitle exceeds 100000 cues.");
            // Display only as text: tags cannot become executable markup.
            cues.Add(new(start, end, cueText.ToString()));
        }
        if (cues.Count == 0) throw new InvalidDataException("No valid subtitle cues found.");
        return cues.OrderBy(c => c.StartTicks).ToImmutableArray();
    }

    private static long ParseTime(string value)
    {
        var match = Time.Match(value);
        if (!match.Success || !long.TryParse(match.Groups[1].Success ? match.Groups[1].Value : "0", out var hours))
            throw new InvalidDataException("Invalid subtitle timestamp.");
        var minutes = int.Parse(match.Groups[2].Value);
        var seconds = int.Parse(match.Groups[3].Value);
        var milliseconds = int.Parse(match.Groups[4].Value);
        if (minutes > 59 || seconds > 59) throw new InvalidDataException("Invalid subtitle timestamp.");
        try { return checked((hours * 3600000 + minutes * 60000 + seconds * 1000 + milliseconds) * 10000); }
        catch (OverflowException ex) { throw new InvalidDataException("Subtitle timestamp is too large.", ex); }
    }
}
