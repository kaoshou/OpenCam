// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectWaveformBucket(float Minimum, float Maximum, float Rms);
public sealed record ProjectWaveformResult(bool HasAudio, bool IsSilent, long SampleCount,
    ImmutableArray<ProjectWaveformBucket> Buckets);
public sealed record ProjectWaveformReply(Guid ClipId, long Revision, ProjectWaveformResult? Data, string? Error = null);
