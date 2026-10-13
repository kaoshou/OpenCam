// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using System.Security.Cryptography;
namespace ScreenRecorder.Infrastructure.Projects;

public static class ProjectSubtitleStore
{
    public static async Task<ProjectSubtitleTrack> ImportAsync(IProjectHandle handle, FileStream source,
        string originalName, string language, CancellationToken ct = default)
    {
        originalName = Path.GetFileName(originalName);
        ProjectValidation.ValidateName(originalName);
        language = ProjectSubtitleLanguage.Normalize(language);
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!source.CanRead || !source.CanSeek || source.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("Subtitle must be a readable regular file of at most 10 MiB.");
        source.Position = 0;
        var bytes = new byte[checked((int)source.Length)];
        await source.ReadExactlyAsync(bytes, ct);
        ProjectSubtitleParser.Parse(bytes, extension);
        var hash = SHA256.HashData(bytes);
        source.Position = 0;
        var secondRead = new byte[bytes.Length];
        await source.ReadExactlyAsync(secondRead, ct);
        if (source.Length != bytes.Length || !hash.SequenceEqual(SHA256.HashData(secondRead)))
            throw new IOException("Subtitle changed during import.");
        var h = JsonProjectStore.Handle(handle);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            if (h.NeedsRecoveryConfirmation) throw new InvalidOperationException("Confirm project recovery before importing subtitles.");
            using var directory = h.Root.OpenChild("subtitles", create: true);
            var id = Guid.NewGuid();
            var leaf = id.ToString("N") + extension;
            var created = false;
            try
            {
                await using (var output = directory.CreateNew(leaf))
                {
                    created = true;
                    await output.WriteAsync(bytes, ct);
                    await output.FlushAsync(ct);
                    output.Flush(true);
                }
                return new(id, originalName, language, "subtitles/" + leaf, bytes.Length,
                    Convert.ToHexString(hash), ProjectSubtitleTimeline.Signature(h.Current));
            }
            catch { if (created) directory.DeleteOwnedFile(leaf); throw; }
        }
        finally { h.Gate.Release(); }
    }

    public static FileStream OpenRead(IProjectHandle handle, ProjectSubtitleTrack track)
    {
        var extension = Path.GetExtension(track.RelativePath).ToLowerInvariant();
        var leaf = track.Id.ToString("N") + extension;
        if (track.Id == Guid.Empty || extension is not (".srt" or ".vtt") ||
            track.RelativePath != "subtitles/" + leaf || track.FileSize is <= 0 or > 10 * 1024 * 1024)
            throw new InvalidDataException("Invalid subtitle asset reference.");
        var h = JsonProjectStore.Handle(handle);
        h.Gate.Wait();
        try
        {
            h.EnsureOpen();
            using var directory = h.Root.OpenChild("subtitles");
            var stream = directory.Read(leaf);
            try
            {
                if (stream.Length != track.FileSize ||
                    Convert.ToHexString(SHA256.HashData(stream)) != track.Sha256)
                    throw new InvalidDataException("Subtitle asset is missing or changed.");
                stream.Position = 0;
                return stream;
            }
            catch { stream.Dispose(); throw; }
        }
        finally { h.Gate.Release(); }
    }
}
