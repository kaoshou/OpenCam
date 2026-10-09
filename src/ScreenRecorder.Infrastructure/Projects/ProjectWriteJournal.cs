// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.Projects;

public sealed record SegmentCommitIntent(Guid OperationId, Guid SourceId, string SessionId,
    string RelativePath, long ExpectedRevision, string Status = "pending", int? CaptureFps = null);

/// <summary>Durable intent ledger; the manifest and its operation IDs resolve incomplete commits.</summary>
public sealed class ProjectWriteJournal
{
    private const string JournalName = "project.journal.json";
    private static void Validate(SegmentCommitIntent intent)
    {
        if (intent is null || intent.OperationId == Guid.Empty || intent.SourceId == Guid.Empty ||
            intent.ExpectedRevision < 0 || intent.Status is not ("pending" or "complete") ||
            intent.CaptureFps is < 1 or > 240 ||
            string.IsNullOrWhiteSpace(intent.SessionId) || intent.SessionId.Length > 200 ||
            intent.SessionId.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n']) >= 0)
            throw new InvalidDataException("Invalid segment commit intent.");
        ProjectPathPolicy.ValidateRelative(intent.RelativePath);
    }

    public async Task WriteIntentAsync(IProjectHandle project, SegmentCommitIntent intent, CancellationToken ct = default)
    {
        Validate(intent);
        var h = JsonProjectStore.Handle(project);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            if (h.NeedsRecoveryConfirmation) throw new InvalidOperationException("Project needs recovery.");
            var items = await ReadAsync(h, ct);
            var existing = items.Find(x => x.OperationId == intent.OperationId);
            if (existing is not null)
            {
                if (existing with { Status = intent.Status } != intent)
                    throw new InvalidDataException("Operation ID was reused for different source content.");
                return;
            }
            if (items.Count >= 10000) throw new InvalidDataException("Journal limit reached.");
            items.Add(intent);
            await WriteAsync(h, items, ct);
        }
        finally { h.Gate.Release(); }
    }

    public async Task<IReadOnlyList<SegmentCommitIntent>> ReadPendingAsync(IProjectHandle project, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(project);
        await h.Gate.WaitAsync(ct);
        try { h.EnsureOpen(); return (await ReadAsync(h, ct)).Where(x => x.Status == "pending").ToArray(); }
        finally { h.Gate.Release(); }
    }

    public async Task CompleteAsync(IProjectHandle project, Guid operationId, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(project);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            if (h.NeedsRecoveryConfirmation) throw new InvalidOperationException("Project needs recovery.");
            var items = await ReadAsync(h, ct);
            var index = items.FindIndex(x => x.OperationId == operationId);
            if (index < 0 || items[index].Status == "complete") return;
            items[index] = items[index] with { Status = "complete" };
            await WriteAsync(h, items, ct);
        }
        finally { h.Gate.Release(); }
    }

    private static async Task<List<SegmentCommitIntent>> ReadAsync(JsonProjectStore.ProjectHandle h, CancellationToken ct)
    {
        string json;
        try { json = await h.Root.ReadTextAsync(JournalName, ct); }
        catch (FileNotFoundException) { return []; }
        var items = JsonSerializer.Deserialize<List<SegmentCommitIntent>>(json, JsonProjectStore.JsonOptions)
            ?? throw new InvalidDataException("Missing journal.");
        if (items.Count > 10000) throw new InvalidDataException("Journal limit reached.");
        foreach (var item in items) Validate(item);
        if (items.Select(x => x.OperationId).Distinct().Count() != items.Count)
            throw new InvalidDataException("Duplicate journal operations.");
        return items;
    }

    private static Task WriteAsync(JsonProjectStore.ProjectHandle h, List<SegmentCommitIntent> items, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(items, JsonProjectStore.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
            throw new InvalidDataException("Journal exceeds metadata limit.");
        return h.Root.WriteTextAsync(JournalName, json, ct);
    }
}
