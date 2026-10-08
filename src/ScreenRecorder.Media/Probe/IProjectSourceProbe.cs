// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Probe;

public sealed record ProjectMediaInfo(ProjectStreamTiming Timing, int Width, int Height,
    string VideoCodec, string? AudioCodec);

public interface IProjectSourceProbe
{
    Task<ProjectMediaInfo> ProbeAsync(Stream source, CancellationToken ct = default);
}
