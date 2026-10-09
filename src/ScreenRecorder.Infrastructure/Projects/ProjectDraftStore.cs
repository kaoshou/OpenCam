// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Bounded metadata only. All access shares the project writer's gate and bound directory.</summary>
internal static class ProjectDraftStore
{
    private const string Name = "project.edits.json";
    private sealed record Envelope(int SchemaVersion, ProjectEditDraft Draft, bool Discarded = false);

    public static async Task<ProjectEditDraft?> ReadAsync(JsonProjectStore.ProjectHandle h, CancellationToken ct)
    {
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            var envelope = await ReadEnvelopeAsync(h, ct);
            if (envelope is null) return null;
            ValidateIdentity(h, envelope);
            if (envelope.Discarded || h.Current.ResolvedDraftId == envelope.Draft.Id) return null;
            ValidateDraft(h, envelope.Draft, allowPreviousBase: true);
            return envelope.Draft;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            // Preserve, never overwrite, one diagnostic copy. If it already exists or
            // the leaf cannot safely be moved, leave the original in place and report it.
            try
            {
                var preserved = h.Root.PublishVerified(Name, "project.edits.invalid.json");
                throw new InvalidDataException($"Invalid recovery draft preserved at {preserved}. The saved project is unchanged.", ex);
            }
            catch (IOException moveError)
            {
                throw new InvalidDataException($"Recovery draft could not be quarantined; preserve or move {Name} manually before creating further drafts. {moveError.Message}", ex);
            }
        }
        finally { h.Gate.Release(); }
    }

    public static async Task SaveAsync(JsonProjectStore.ProjectHandle h, ProjectEditDraft draft, CancellationToken ct)
    {
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            ValidateDraft(h, draft);
            var old = await ReadEnvelopeAsync(h, ct);
            if (old is not null)
            {
                ValidateIdentity(h, old);
                if (old.Draft.Id == draft.Id && (old.Discarded || h.Current.ResolvedDraftId == draft.Id ||
                    draft.Sequence <= old.Draft.Sequence))
                    throw new InvalidOperationException("Stale or resolved edit draft.");
                if (old.Draft.Id != draft.Id && !old.Discarded && h.Current.ResolvedDraftId != old.Draft.Id &&
                    old.Draft.BaseRevision == h.Current.Revision)
                    throw new InvalidOperationException("Resolve the existing edit draft first.");
            }
            var json = Serialize(new(1, draft));
            if (old is not null) await h.Root.WriteTextAsync(Name + ".bak", Serialize(old), ct);
            await h.Root.WriteTextAsync(Name, json, ct);
        }
        finally { h.Gate.Release(); }
    }

    public static async Task DiscardAsync(JsonProjectStore.ProjectHandle h, Guid id, CancellationToken ct)
    {
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            var old = await ReadEnvelopeAsync(h, ct);
            if (old is null) return;
            ValidateIdentity(h, old);
            if (id != old.Draft.Id) throw new InvalidOperationException("Draft identity changed.");
            // Retain the bounded tombstone. A stale backup can never resurrect discarded edits.
            await h.Root.WriteTextAsync(Name, Serialize(old with { Discarded = true }), ct);
        }
        finally { h.Gate.Release(); }
    }

    private static async Task<Envelope?> ReadEnvelopeAsync(JsonProjectStore.ProjectHandle h, CancellationToken ct)
    {
        string json;
        try { json = await h.Root.ReadTextAsync(Name, ct); }
        catch (FileNotFoundException) { return null; }
        if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
            throw new InvalidDataException("Edit draft exceeds metadata limit.");
        return JsonSerializer.Deserialize<Envelope>(json, JsonProjectStore.JsonOptions)
            ?? throw new InvalidDataException("Missing edit draft.");
    }

    private static void ValidateIdentity(JsonProjectStore.ProjectHandle h, Envelope e)
    {
        if (e.SchemaVersion != 1 || e.Draft is null || e.Draft.Id == Guid.Empty ||
            e.Draft.ProjectId != h.Current.ProjectId || e.Draft.Sequence < 1 || e.Draft.BaseRevision < 0)
            throw new InvalidDataException("Invalid edit draft identity.");
    }

    private static void ValidateDraft(JsonProjectStore.ProjectHandle h, ProjectEditDraft d, bool allowPreviousBase = false)
    {
        ValidateIdentity(h, new(1, d));
        if (h.NeedsRecoveryConfirmation || (d.BaseRevision != h.Current.Revision &&
            !(allowPreviousBase && d.BaseRevision == h.Current.RecoveryDraftBaseRevision)))
            throw new InvalidDataException("Edit draft does not match the saved project revision.");
        ProjectValidation.Validate(h.Current with { Name = d.Name, Clips = d.Clips });
    }

    private static string Serialize(Envelope e)
    {
        var json = JsonSerializer.Serialize(e, JsonProjectStore.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
            throw new InvalidDataException("Edit draft exceeds metadata limit.");
        return json;
    }
}
