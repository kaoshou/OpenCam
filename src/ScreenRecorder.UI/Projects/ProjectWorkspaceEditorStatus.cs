// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects.Editor;
namespace ScreenRecorder.UI.Projects;
public sealed partial class ProjectWorkspaceViewModel
{
    private bool _projectSaveFailed;
    private Guid? _seenExportId;
    private (Guid? Project,Guid? Server) _exportOwner;
    public string CurrentExportStatusText => State.Export is not { } export ? Strings["EditorNoExport"] :
        export.State != RecordingExportState.Succeeded ? ExportStatusText :
        Strings[export.Revision==State.Revision && _exportOwner==(State.ProjectId,State.ServerInstanceId)
            ?"EditorCurrentExport":"EditorOlderExport"]+" · "+export.FinalPath;
    public string PreviousFrameHint => Strings["EditorPreviousFrame"]+" · "+EditorKeyboardCommands.Hint("framePrevious",OperatingSystem.IsMacOS());
    public string NextFrameHint => Strings["EditorNextFrame"]+" · "+EditorKeyboardCommands.Hint("frameNext",OperatingSystem.IsMacOS());
    public string PlaybackHint => PlaybackButtonText+" · "+EditorKeyboardCommands.Hint("playPause",OperatingSystem.IsMacOS());
    public string MarkInHint => Strings["ProjectRangeStart"]+" · "+EditorKeyboardCommands.Hint("markIn",OperatingSystem.IsMacOS());
    public string MarkOutHint => Strings["ProjectRangeEnd"]+" · "+EditorKeyboardCommands.Hint("markOut",OperatingSystem.IsMacOS());
    public string EditorKeyboardHelpText => string.Join("\n",new[]{PlaybackHint,PreviousFrameHint,NextFrameHint,
        Strings["EditorSecondSeek"]+" · "+EditorKeyboardCommands.Hint("secondPrevious",OperatingSystem.IsMacOS())+" / "+EditorKeyboardCommands.Hint("secondNext",OperatingSystem.IsMacOS()),
        MarkInHint,MarkOutHint,Strings["ProjectSave"]+" · "+EditorKeyboardCommands.Hint("save",OperatingSystem.IsMacOS()),
        Strings["ProjectUndo"]+" · "+EditorKeyboardCommands.Hint("undo",OperatingSystem.IsMacOS()),
        Strings["ProjectRedo"]+" · "+EditorKeyboardCommands.Hint("redo",OperatingSystem.IsMacOS()),Strings["EditorKeyboardScope"]});
    public string SelectedClipAudioText => SelectedClip is not { } clip ? "" :
        $"{Strings["EditorAudioEffects"]} · {(clip.Muted?Strings["PreviewSoundOff"]:$"{clip.Volume*100:0}%")} · ↑ {TimeSpan.FromTicks(clip.FadeInTicks).TotalSeconds:0.###} s / ↓ {TimeSpan.FromTicks(clip.FadeOutTicks).TotalSeconds:0.###} s";
}
