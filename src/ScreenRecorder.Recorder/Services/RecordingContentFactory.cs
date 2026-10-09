// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Creates durable editing content before capture can acquire a session.</summary>
public sealed class RecordingContentFactory(IProjectStore store, TimeProvider clock)
{
    public async Task<IProjectHandle> CreateAsync(string outputDirectory, CancellationToken ct = default, string? projectName = null)
    {
        ct.ThrowIfCancellationRequested();
        var name = string.IsNullOrWhiteSpace(projectName)
            ? clock.GetLocalNow().ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)
            : projectName.Trim();
        ProjectValidation.ValidateName(name);
        using var output = BoundDirectory.Open(outputDirectory, create: true);
        using var contents = output.OpenChild("OpenCam Recordings", create: true);
        return await store.CreateAsync(contents.CurrentPath, name, ct);
    }
}
