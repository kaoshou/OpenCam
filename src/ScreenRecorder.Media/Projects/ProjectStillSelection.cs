// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Timeline stills hold the displayed output frame; cards show their own retained source.</summary>
public sealed record ProjectStillSelection(ProjectRenderPlan Plan, int Index, long Frame)
{
    public static ProjectStillSelection Create(RecordingProject project, long ticks, Guid thumbnailClipId = default)
    {
        var plan = ProjectRenderPlan.Create(project);
        if (ticks < 0 || ticks >= plan.DurationTicks) throw new InvalidDataException("Outside timeline.");
        if (thumbnailClipId != Guid.Empty)
        {
            var clip = project.Clips.FirstOrDefault(c => c.Id == thumbnailClipId)
                ?? throw new InvalidDataException("Unknown thumbnail clip.");
            // A valid clip shorter than one output frame still needs a source card.
            return new(ProjectRenderPlan.Create(project with { Clips = [clip] }), 0, 0);
        }
        var frame = (long)((BigInteger)ticks * plan.Canvas.Fps.Numerator /
            ((BigInteger)TimeSpan.TicksPerSecond * plan.Canvas.Fps.Denominator));
        var owner = plan.LocateVideoFrame(frame) ?? throw new InvalidDataException("No output frame.");
        var index = 0;
        while (plan.Clips[index].Clip.Id != owner.ClipId) index++;
        return new(plan, index, frame - plan.Clips[index].StartFrame);
    }
}
