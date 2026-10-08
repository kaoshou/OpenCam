// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Publishes only a verified, uniquely named output, never modifying original sources.</summary>
public interface IRecordingContentExporter
{
    Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
        string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct);
}
