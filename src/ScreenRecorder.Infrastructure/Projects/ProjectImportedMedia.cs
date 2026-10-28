// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Owns one unpredictable staging leaf; originals are opened read-only.</summary>
public sealed class ProjectImportedMedia : IDisposable
{
    private readonly BoundDirectory _directory;
    private readonly string _temporary;
    private bool _published;
    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath => $"sources/{Id:N}.mkv";
    public FileStream Output { get; }

    public ProjectImportedMedia(ScreenRecorder.Core.Projects.IProjectHandle handle)
    {
        _directory = BoundDirectory.Open(Path.Combine(handle.ProjectDirectory, "sources"));
        _temporary = $"{Id:N}.importing";
        try { Output = _directory.CreateNew(_temporary); }
        catch { _directory.Dispose(); throw; }
    }

    public static FileStream OpenOriginal(string path) =>
        ScreenRecorder.Infrastructure.Recovery.RecoverySourceFile.Open(Path.GetFullPath(path));

    public FileStream OpenStaged() => _directory.Read(_temporary);
    public void Publish()
    {
        Output.Dispose();
        _directory.PublishVerified(_temporary, $"{Id:N}.mkv");
        _published = true;
    }

    public void Dispose()
    {
        Output.Dispose();
        try { if (!_published) _directory.DeleteOwnedFile(_temporary); }
        finally { _directory.Dispose(); }
    }
}
