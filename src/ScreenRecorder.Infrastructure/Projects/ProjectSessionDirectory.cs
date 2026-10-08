// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Holds the recording destination, including while the project root is renamed.</summary>
public sealed class ProjectSessionDirectory : IDisposable
{
    private readonly IProjectHandle _project;
    private readonly BoundDirectory _sessions, _catalog;
    public BoundDirectory Directory { get; }
    private readonly string _sessionId;
    private ProjectSessionDirectory(IProjectHandle project, BoundDirectory sessions,
        BoundDirectory catalog, BoundDirectory directory, string id)
        => (_project, _sessions, _catalog, Directory, _sessionId) = (project, sessions, catalog, directory, id);

    public static ProjectSessionDirectory Create(IProjectHandle project, string id)
    {
        var sessions = JsonProjectStore.Handle(project).Root.OpenChild("sessions");
        BoundDirectory? catalog = null;
        try
        {
            catalog = sessions.OpenChild("Sessions", create: true);
            return new(project, sessions, catalog, catalog.OpenChild(id, create: true, exclusive: true), id);
        }
        catch { catalog?.Dispose(); sessions.Dispose(); throw; }
    }

    public void Refresh(RecordingSession session)
    {
        if (session.ProjectId != _project.Current.ProjectId || session.SessionId != _sessionId)
            throw new InvalidOperationException("Project session identity changed.");
        var root = _project.ProjectDirectory;
        var path = Directory.CurrentPath;
        // A whole-project move is safe; moving a session out of its project is not.
        if (_sessions.CurrentPath != Path.Combine(root, "sessions") ||
            _catalog.CurrentPath != Path.Combine(root, "sessions", "Sessions") ||
            path != Path.Combine(root, "sessions", "Sessions", _sessionId))
            throw new IOException("Recording session moved outside the open project.");
        session.WorkingFilePath = Path.Combine(path, Path.GetFileName(session.WorkingFilePath));
        session.SegmentFilePaths = session.SegmentFilePaths.Select(p => Path.Combine(path, Path.GetFileName(p))).ToList();
        session.WorkingDirectory = path;
        session.LogFilePath = Path.Combine(path, "session.log");
        session.Configuration.OutputDirectory = Path.Combine(root, "sessions");
    }

    public void Dispose() { Directory.Dispose(); _catalog.Dispose(); _sessions.Dispose(); }
}
