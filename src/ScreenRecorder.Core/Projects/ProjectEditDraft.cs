// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectEditDraft(Guid Id, Guid ProjectId, long BaseRevision, long Sequence,
    string Name, ImmutableArray<ProjectClip> Clips);
