// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public static class ProjectSubtitleLanguage
{
    private static readonly System.Text.RegularExpressions.Regex Token = new(
        @"(?:^|[._\s-])(?<language>zh[-_](?:tw|cn|hk)|(?:en|ko|ja|fr|de|es|vi|th|id|ms|pt|ru|ar)(?:[-_][a-z]{2})?)(?=$|[._\s-])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string? Infer(string fileName)
    {
        var match = Token.Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? Normalize(match.Groups["language"].Value) : null;
    }

    public static string Normalize(string language)
    {
        var parts = language.Replace('_', '-').Split('-');
        if (parts.Length == 0 || parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter) ||
            parts.Any(p => p.Length is < 2 or > 8 || !p.All(char.IsAsciiLetterOrDigit)))
            throw new InvalidDataException("Use a valid subtitle language code.");
        parts[0] = parts[0].ToLowerInvariant();
        for (var i = 1; i < parts.Length; i++)
            parts[i] = parts[i].Length == 2 ? parts[i].ToUpperInvariant() :
                parts[i].Length == 4 ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant() : parts[i].ToLowerInvariant();
        return string.Join('-', parts);
    }
}
