// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Reads only project-owned sessions through the project's pinned directory handles.</summary>
public static class ProjectSessionCatalog
{
    public static async Task<IReadOnlyList<RecordingSession>> ReadAsync(IProjectHandle project, CancellationToken ct = default)
    {
        var handle = JsonProjectStore.Handle(project);
        await handle.Gate.WaitAsync(ct);
        try
        {
            handle.EnsureOpen();
            using var sessions = handle.Root.OpenChild("sessions");
            if (!Directory.Exists(Path.Combine(sessions.CurrentPath, "Sessions"))) return [];
            using var catalog = sessions.OpenChild("Sessions");
            var result = new List<RecordingSession>();
            // Enumeration supplies labels only. Each label is then opened relative to the bound parent.
            foreach (var entry in Directory.EnumerateDirectories(catalog.CurrentPath))
            {
                ct.ThrowIfCancellationRequested();
                if (result.Count >= 1000) throw new InvalidDataException("Too many project sessions.");
                var label = Path.GetFileName(entry);
                using var dir = catalog.OpenChild(label);
                var json = await dir.ReadTextAsync("session.json", ct);
                var session = JsonSerializer.Deserialize<RecordingSession>(json, JsonProjectStore.JsonOptions)
                    ?? throw new InvalidDataException($"Invalid session: {label}");
                if (session.ProjectId != project.Current.ProjectId || session.SessionId != label ||
                    session.CompletionPolicy != ProjectCompletionPolicy.KeepProjectSources)
                    throw new InvalidDataException($"Session ownership mismatch: {label}");
                if (session.SegmentFilePaths is null || session.SegmentFilePaths.Count > 10000)
                    throw new InvalidDataException("Invalid project segment list.");
                var paths = new List<string>();
                var leaves = session.SegmentFilePaths.ToList();
                if (!string.IsNullOrEmpty(session.WorkingFilePath) && !leaves.Contains(session.WorkingFilePath))
                {
                    // Startup metadata names the in-flight file before it can be added to the committed list.
                    // Missing means the engine never created it; any existing invalid file must surface as an error.
                    try { using var pending = dir.Read(session.WorkingFilePath); leaves.Add(session.WorkingFilePath); }
                    catch (FileNotFoundException) { }
                }
                foreach (var leaf in leaves)
                {
                    if (Path.GetFileName(leaf) != leaf || !leaf.StartsWith("segment_", StringComparison.Ordinal) ||
                        !leaf.EndsWith(".mkv", StringComparison.Ordinal) ||
                        !int.TryParse(leaf.AsSpan(8, leaf.Length - 12), out var index) || index < 0)
                        throw new InvalidDataException($"Invalid project session source: {leaf}");
                    using var source = dir.Read(leaf);
                    paths.Add(Path.Combine(dir.CurrentPath, leaf));
                }
                session.WorkingDirectory = dir.CurrentPath;
                session.SegmentFilePaths = paths;
                if (paths.Count > 0) session.WorkingFilePath = paths[^1];
                if (session.State != RecordingState.Completed) session.State = RecordingState.Interrupted;
                result.Add(session);
            }
            return result.OrderBy(s => s.StartTime).ToArray();
        }
        finally { handle.Gate.Release(); }
    }
}
