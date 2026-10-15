// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.Projects;

public static class ProjectBookmarkStore
{
    private const string Filename = "opencam-bookmarks.json";
    public static async Task<ProjectBookmarkDocument> ReadAsync(IProjectHandle owner, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(owner);
        await h.Gate.WaitAsync(ct);
        try { h.EnsureOpen(); return await ReadCoreAsync(h, ct); }
        finally { h.Gate.Release(); }
    }

    private static async Task<ProjectBookmarkDocument> ReadCoreAsync(JsonProjectStore.ProjectHandle h, CancellationToken ct)
    {
        string json;
        try { json = await h.Root.ReadTextAsync(Filename, ct); }
        catch (FileNotFoundException) { return new(1, h.Current.ProjectId, 0, []); }
        var doc = JsonSerializer.Deserialize<ProjectBookmarkDocument>(json, JsonProjectStore.JsonOptions)
            ?? throw new InvalidDataException("Missing bookmark document.");
        ProjectBookmarks.Validate(doc);
        if (doc.ProjectId != h.Current.ProjectId) throw new InvalidDataException("Bookmark project identity mismatch.");
        return doc;
    }

    public static async Task WriteAsync(IProjectHandle owner, ProjectBookmarkDocument document, long expectedRevision, CancellationToken ct = default)
    {
        ProjectBookmarks.Validate(document);
        var h = JsonProjectStore.Handle(owner);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            var old = await ReadCoreAsync(h, ct);
            if (document.ProjectId != h.Current.ProjectId || old.Revision != expectedRevision || document.Revision != checked(expectedRevision + 1))
                throw new InvalidOperationException("Bookmark revision changed; refresh before editing.");
            var json = JsonSerializer.Serialize(document, JsonProjectStore.JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new InvalidDataException("Bookmarks exceed 4 MiB.");
            await h.Root.WriteTextAsync(Filename, json, ct);
        }
        finally { h.Gate.Release(); }
    }
}
