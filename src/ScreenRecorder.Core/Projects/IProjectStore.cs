// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public sealed record ProjectSaveReceipt(Guid ProjectId, long Revision, DateTimeOffset CommittedAt);

public interface IProjectStore
{
    Task<IProjectHandle> CreateAsync(string parentDirectory, string name, CancellationToken ct = default);
    Task<IProjectHandle> OpenAsync(string manifestPath, CancellationToken ct = default);
}

public interface IProjectHandle : IAsyncDisposable
{
    Task RestoreBackupAsync(CancellationToken ct = default);
    RecordingProject Current { get; }
    string ProjectDirectory { get; }
    bool NeedsRecoveryConfirmation { get; }
    Task<ProjectSaveReceipt> SaveAsync(RecordingProject next, long expectedRevision, CancellationToken ct = default);
}
