// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.ProjectPreviewProbe;

public sealed record PreviewProbeFrame(long RequestedPts, long ActualPts, string FrameHash);
public interface IPreviewCandidate : IAsyncDisposable
{
    Task OpenAsync(Stream source, CancellationToken ct);
    Task<PreviewProbeFrame> SeekAsync(long sourcePts, ProjectRational timeBase, CancellationToken ct);
    Task PlayAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public sealed record ProbeCheck(string Name, string Status, string Detail);
public sealed record PreviewProbeResult(string Candidate, string Status, IReadOnlyList<ProbeCheck> Checks);
