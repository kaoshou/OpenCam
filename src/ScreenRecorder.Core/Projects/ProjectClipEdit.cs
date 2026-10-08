// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;

namespace ScreenRecorder.Core.Projects;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProjectClipEdit.Trim), "trim")]
[JsonDerivedType(typeof(ProjectClipEdit.Split), "split")]
[JsonDerivedType(typeof(ProjectClipEdit.Remove), "remove")]
[JsonDerivedType(typeof(ProjectClipEdit.Move), "move")]
[JsonDerivedType(typeof(ProjectClipEdit.Group), "group")]
[JsonDerivedType(typeof(ProjectClipEdit.Ungroup), "ungroup")]
[JsonDerivedType(typeof(ProjectClipEdit.RemoveRange), "removeRange")]
public abstract record ProjectClipEdit
{
    private ProjectClipEdit() { }
    public sealed record Trim(Guid ClipId, long InPts, long OutPts) : ProjectClipEdit;
    public sealed record Split(Guid ClipId, long AtPts, Guid RightClipId) : ProjectClipEdit;
    public sealed record Remove(Guid ClipId) : ProjectClipEdit;
    public sealed record Move(Guid ClipId, Guid? BeforeClipId) : ProjectClipEdit;
    public sealed record Group(Guid FirstClipId, Guid LastClipId, Guid GroupId) : ProjectClipEdit;
    public sealed record Ungroup(Guid GroupId) : ProjectClipEdit;
    public sealed record RemoveRange(long StartTicks, long EndTicks) : ProjectClipEdit;
}
