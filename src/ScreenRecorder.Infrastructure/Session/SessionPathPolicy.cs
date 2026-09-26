// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Infrastructure.Session;

/// <summary>Metadata is descriptive, never authority to read or write another directory.</summary>
public static class SessionPathPolicy
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static void RejectLink(string path)
    {
        // LinkTarget also detects dangling links (File.Exists would miss them).
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null ||
            (File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked recording files/directories are not supported for recovery.");
    }

    public static string DirectoryPath(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        RejectLink(full);
        var parent = Path.GetDirectoryName(full);
        if (parent != null) RejectLink(parent);
        return full;
    }

    public static string SegmentPath(string directory, string path)
    {
        var full = Path.GetFullPath(path, directory);
        if (!string.Equals(Path.GetDirectoryName(full), directory, Comparison) ||
            !string.Equals(Path.GetExtension(full), ".mkv", StringComparison.OrdinalIgnoreCase) ||
            full.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidDataException("Recovery segment must be an MKV inside its session directory.");
        RejectLink(full);
        return full;
    }

    public static RecordingSession? Bind(RecordingSession? session, string directory)
    {
        if (session == null) return null;
        directory = DirectoryPath(directory);
        // Session IDs are labels, not paths. Keep legitimate historical labels.
        if (string.IsNullOrWhiteSpace(session.SessionId) || session.SessionId is "." or ".." ||
            session.SessionId.IndexOfAny(['/', '\\', '\r', '\n', '\0', ':']) >= 0)
            throw new InvalidDataException("Invalid recording session identifier.");
        session.WorkingDirectory = directory;
        session.SegmentFilePaths = (session.SegmentFilePaths ?? [])
            .Select(path => SegmentPath(directory, path)).ToList();
        if (!string.IsNullOrWhiteSpace(session.WorkingFilePath))
            session.WorkingFilePath = SegmentPath(directory, session.WorkingFilePath);
        return session;
    }
}
