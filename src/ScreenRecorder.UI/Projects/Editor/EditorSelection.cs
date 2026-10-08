// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Projects.Editor;

public sealed record EditorSelection(Guid? ClipId, long PlayheadTicks, long? RangeStartTicks, long? RangeEndTicks);
