// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

public sealed class JsonProjectStore : IProjectStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = ProjectValidation.MaximumJsonDepth };

    public sealed record Summary(string Path, string Name, DateTime Modified)
    {
        public override string ToString() => Modified == DateTime.MinValue ? Name : $"{Name} · {Modified:yyyy-MM-dd HH:mm}";
    }

    public static async Task<Summary> ReadSummaryAsync(string manifestPath, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(manifestPath);
        if (Path.GetFileName(full) != "project.opencam") throw new InvalidDataException("Invalid project filename.");
        using var root = BoundDirectory.Open(Path.GetDirectoryName(full)!);
        var project = Parse(await root.ReadTextAsync("project.opencam", ct));
        return new(full, project.Name, File.GetLastWriteTime(full));
    }

    public async Task<IProjectHandle> CreateAsync(string parentDirectory, string name, CancellationToken ct = default)
    {
        ProjectValidation.ValidateName(name);
        ct.ThrowIfCancellationRequested();
        var parent = BoundDirectory.Open(parentDirectory);
        BoundDirectory root;
        try { root = parent.OpenChild($"OpenCam-{ProjectNaming.FileStem(name)}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}", create: true, exclusive: true); }
        catch { parent.Dispose(); throw; }
        FileStream? claim = null;
        try
        {
            claim = root.Claim(".project.lock");
            foreach (var child in new[] { "sources", "sessions", "cache", "exports" })
                using (root.OpenChild(child, create: true)) { }
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = name };
            await root.WriteTextAsync("project.opencam", Serialize(project), ct);
            return new ProjectHandle(root, claim, project, false, parent);
        }
        catch { claim?.Dispose(); root.Dispose(); parent.Dispose(); throw; }
    }

    public async Task<IProjectHandle> OpenAsync(string manifestPath, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(manifestPath);
        if (!string.Equals(Path.GetFileName(full), "project.opencam", StringComparison.Ordinal))
            throw new InvalidDataException("Select the project's project.opencam file.");
        var root = BoundDirectory.Open(Path.GetDirectoryName(full)!);
        FileStream? claim = null;
        try
        {
            claim = root.Claim(".project.lock");
            RecordingProject project;
            var recovery = false;
            try { project = Parse(await root.ReadTextAsync("project.opencam", ct)); }
            catch (Exception error) when (error is JsonException or InvalidDataException or FileNotFoundException)
            {
                // Refuse unsupported schemas rather than rolling back to an old readable backup.
                project = Parse(await root.ReadTextAsync("project.opencam.bak", ct));
                recovery = true;
            }
            return new ProjectHandle(root, claim, project, recovery);
        }
        catch { claim?.Dispose(); root.Dispose(); throw; }
    }

    internal static RecordingProject Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
            throw new InvalidDataException("Project exceeds metadata limit.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = ProjectValidation.MaximumJsonDepth });
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("SchemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
            throw new InvalidDataException("Missing project schema.");
        if (version != 1) throw new NotSupportedException("Project schema is not supported; no files have been changed.");
        var project = JsonSerializer.Deserialize<RecordingProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Missing project.");
        ProjectValidation.Validate(project);
        foreach (var source in project.Sources) ProjectPathPolicy.ValidateRelative(source.RelativePath);
        return project;
    }

    internal static string Serialize(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        foreach (var source in project.Sources) ProjectPathPolicy.ValidateRelative(source.RelativePath);
        var json = JsonSerializer.Serialize(project, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > ProjectValidation.MaximumManifestBytes)
            throw new InvalidDataException("Project exceeds metadata limit.");
        return json;
    }

    internal sealed class ProjectHandle(BoundDirectory root, FileStream claim, RecordingProject current,
        bool needsRecovery, BoundDirectory? parentLease = null) : IProjectHandle
    {
        internal BoundDirectory Root { get; } = root;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        private bool _disposed;
        public RecordingProject Current { get; private set; } = current;
        public string ProjectDirectory => Root.CurrentPath;
        public bool NeedsRecoveryConfirmation { get; private set; } = needsRecovery;
        internal void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);

        public async Task RestoreBackupAsync(CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct);
            try
            {
                EnsureOpen();
                if (!NeedsRecoveryConfirmation) throw new InvalidOperationException("No pending backup recovery.");
                // Do not replace a newly repaired/updated manifest, especially a future schema.
                try
                {
                    var currentText = await Root.ReadTextAsync("project.opencam", ct);
                    try { Parse(currentText); throw new InvalidOperationException("Primary project changed; close and reopen before recovery."); }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException) { }
                }
                catch (FileNotFoundException) { }
                try { await Root.CopyToNewAsync("project.opencam", "project.opencam.damaged-" + Guid.NewGuid().ToString("N"), ct); }
                catch (FileNotFoundException) { }
                await Root.WriteTextAsync("project.opencam", Serialize(Current), ct);
                NeedsRecoveryConfirmation = false;
            }
            finally { Gate.Release(); }
        }

        public async Task<ProjectSaveReceipt> SaveAsync(RecordingProject next, long expectedRevision, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct);
            try
            {
                EnsureOpen();
                if (NeedsRecoveryConfirmation) throw new InvalidOperationException("Backup recovery requires explicit confirmation.");
                if (expectedRevision != Current.Revision || next.Revision <= Current.Revision)
                    throw new InvalidOperationException("Stale project revision.");
                if (next.ProjectId != Current.ProjectId) throw new InvalidDataException("Project identity changed.");
                if (Current.Sources.Any(old => !next.Sources.Contains(old)))
                    throw new InvalidDataException("Original sources cannot be removed or changed by saving edits.");
                var json = Serialize(next);
                var previousJson = await Root.ReadTextAsync("project.opencam", ct);
                var previous = Parse(previousJson);
                if (previous.ProjectId != Current.ProjectId || previous.Revision != Current.Revision)
                    throw new InvalidOperationException("Project changed outside this writer.");
                await Root.WriteTextAsync("project.opencam.bak", previousJson, ct);
                await Root.WriteTextAsync("project.opencam", json, ct);
                // Once rename committed, cancellation must not turn a committed write into a failure.
                Current = next;
                return new(Current.ProjectId, Current.Revision, DateTimeOffset.UtcNow);
            }
            finally { Gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (_disposed) return;
                _disposed = true;
                claim.Dispose();
                Root.Dispose();
                parentLease?.Dispose();
            }
            finally { Gate.Release(); }
        }
    }

    internal static ProjectHandle Handle(IProjectHandle handle)
    {
        if (handle is not ProjectHandle bound) throw new InvalidOperationException("A bound project handle is required.");
        bound.EnsureOpen();
        return bound;
    }
}
