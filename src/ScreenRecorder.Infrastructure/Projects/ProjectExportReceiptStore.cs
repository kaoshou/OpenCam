// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

public static class ProjectExportReceiptStore
{
    public static async Task WriteAsync(IProjectHandle owner, RecordingProject snapshot, string outputPath, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(owner);
        var path = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid export receipt target.");
        using var directory = BoundDirectory.Open(Path.GetDirectoryName(path)!);
        using var file = directory.Read(Path.GetFileName(path));
        if (file.Length <= 0) throw new InvalidDataException("Empty export.");
        var receipt = new ProjectExportReceipt(ProjectExportFingerprint.Compute(snapshot, ProjectExportFingerprint.Profile),
            path, file.Length, Convert.ToHexString(await SHA256.HashDataAsync(file, ct)));
        await h.Gate.WaitAsync(ct);
        try { h.EnsureOpen(); await h.Root.WriteTextAsync("project.export.json", JsonSerializer.Serialize(receipt), ct); }
        finally { h.Gate.Release(); }
    }

    public static async Task<ProjectExportReceipt?> FindVerifiedAsync(IProjectHandle owner, RecordingProject snapshot,
        string outputDirectory, CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(owner);
        string json;
        await h.Gate.WaitAsync(ct);
        try { h.EnsureOpen(); json = await h.Root.ReadTextAsync("project.export.json", ct); }
        catch (FileNotFoundException) { return null; }
        finally { h.Gate.Release(); }
        try
        {
            var receipt = JsonSerializer.Deserialize<ProjectExportReceipt>(json, JsonProjectStore.JsonOptions);
            if (receipt is null || receipt.Length <= 0 || receipt.Sha256 is not { Length: 64 } ||
                receipt.ContentFingerprint != ProjectExportFingerprint.Compute(snapshot, ProjectExportFingerprint.Profile) ||
                !Path.IsPathFullyQualified(receipt.Path) || Path.GetExtension(receipt.Path).ToLowerInvariant() != ".mp4") return null;
            var path = Path.GetFullPath(receipt.Path);
            if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
            using var directory = BoundDirectory.Open(outputDirectory);
            using var file = directory.Read(Path.GetFileName(path));
            if (file.Length != receipt.Length) return null;
            return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), receipt.Sha256,
                StringComparison.OrdinalIgnoreCase) ? receipt : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
}
