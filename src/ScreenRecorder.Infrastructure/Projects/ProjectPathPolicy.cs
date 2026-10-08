// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

public static class ProjectPathPolicy
{
    internal static string[] ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
            path.IndexOfAny(['\\', ':', '\0', '\r', '\n']) >= 0 || Path.IsPathRooted(path))
            throw new InvalidDataException("Invalid project source path.");
        var parts = path.Split('/');
        if (parts.Length < 2 || parts[0] is not ("sources" or "sessions") ||
            parts.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) ||
            !string.Equals(Path.GetExtension(parts[^1]), ".mkv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source must be an MKV within project sources or sessions.");
        return parts;
    }

    public static FileStream OpenSource(IProjectHandle project, string relativePath)
    {
        var parts = ValidateRelative(relativePath);
        var handle = JsonProjectStore.Handle(project);
        var opened = new List<BoundDirectory>();
        try
        {
            var dir = handle.Root;
            foreach (var part in parts[..^1])
            {
                dir = dir.OpenChild(part);
                opened.Add(dir);
            }
            return dir.Read(parts[^1]);
        }
        finally { foreach (var dir in opened.AsEnumerable().Reverse()) dir.Dispose(); }
    }
}
