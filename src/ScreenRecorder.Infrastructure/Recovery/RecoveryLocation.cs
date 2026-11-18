// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Recovery;

public sealed record RecoveryLocation(string Root, string? SessionDirectory = null)
{
    public bool Includes(string directory) => SessionDirectory is null ||
        string.Equals(SessionDirectory, directory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static RecoveryLocation FromSelection(string selection)
    {
        var path = SessionPathPolicy.DirectoryPath(selection);
        if (File.Exists(path))
        {
            if (Path.GetFileName(path) != "project.opencam") throw new InvalidDataException("Select project.opencam.");
            path = Path.GetDirectoryName(path)!;
        }
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        if (File.Exists(Path.Combine(path, "project.opencam")) || Directory.Exists(Path.Combine(path, "sessions", "Sessions")))
            return new(SessionPathPolicy.DirectoryPath(Path.Combine(path, "sessions")));

        if (Directory.Exists(Path.Combine(path, "Sessions"))) return new(path);
        var parent = Directory.GetParent(path);
        if (parent != null && string.Equals(Path.GetFileName(path), "Sessions", StringComparison.OrdinalIgnoreCase))
            return new(parent.FullName);
        if (parent?.Parent != null && string.Equals(parent.Name, "Sessions", StringComparison.OrdinalIgnoreCase))
            return new(parent.Parent.FullName, path);
        return new(path);
    }
}
