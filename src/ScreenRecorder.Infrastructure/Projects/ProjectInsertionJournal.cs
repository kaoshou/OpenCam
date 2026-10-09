// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.Projects;

public sealed record ProjectInsertionIntent(Guid OperationId, string SessionId, ProjectInsertionAnchor Anchor);

/// <summary>Prepared before capture. A source in the same manifest is the durable commit receipt.</summary>
public sealed class ProjectInsertionJournal
{
    private const string FileName = "project.insertions.json";

    public async Task PrepareAsync(IProjectHandle project, ProjectInsertionIntent intent, CancellationToken ct = default)
    {
        Validate(intent);
        var h = JsonProjectStore.Handle(project);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            if (h.NeedsRecoveryConfirmation) throw new InvalidOperationException("Project needs recovery.");
            var items = await ReadAsync(h, ct);
            var old = items.Find(x => x.OperationId == intent.OperationId || x.SessionId == intent.SessionId);
            if (old is not null)
            {
                if (old != intent) throw new InvalidDataException("Insertion identity reused.");
                return;
            }
            if (intent.Anchor != ProjectInsertion.Anchor(h.Current, intent.Anchor.TimelineTicks))
                throw new InvalidDataException("Insertion anchor is stale.");
            if (items.Count >= 1000) throw new InvalidDataException("Insertion journal limit reached.");
            items.Add(intent);
            var json = JsonSerializer.Serialize(items, JsonProjectStore.JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
                throw new InvalidDataException("Insertion journal is too large.");
            await h.Root.WriteTextAsync(FileName, json, ct);
        }
        finally { h.Gate.Release(); }
    }

    public async Task<ProjectInsertionIntent?> FindAsync(IProjectHandle project, string sessionId, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(project);
        await h.Gate.WaitAsync(ct);
        try { h.EnsureOpen(); return (await ReadAsync(h, ct)).Find(x => x.SessionId == sessionId); }
        finally { h.Gate.Release(); }
    }

    private static async Task<List<ProjectInsertionIntent>> ReadAsync(JsonProjectStore.ProjectHandle h, CancellationToken ct)
    {
        string json;
        try { json = await h.Root.ReadTextAsync(FileName, ct); }
        catch (FileNotFoundException) { return []; }
        var items = JsonSerializer.Deserialize<List<ProjectInsertionIntent>>(json, JsonProjectStore.JsonOptions)
            ?? throw new InvalidDataException("Missing insertion journal.");
        if (items.Count > 1000) throw new InvalidDataException("Insertion journal limit reached.");
        foreach (var item in items) Validate(item);
        if (items.Select(x => x.OperationId).Distinct().Count() != items.Count ||
            items.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidDataException("Duplicate insertion identities.");
        return items;
    }

    private static void Validate(ProjectInsertionIntent intent)
    {
        if (intent is null || intent.OperationId == Guid.Empty || intent.Anchor is null ||
            intent.Anchor.ProjectId == Guid.Empty || intent.Anchor.Revision < 0 || intent.Anchor.TimelineTicks < 0 ||
            intent.SessionId != "insert_" + intent.OperationId.ToString("N"))
            throw new InvalidDataException("Invalid recording insertion intent.");
    }
}
