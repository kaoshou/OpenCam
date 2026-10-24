// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private Guid? _pointerProject;
    private ProjectPointerSettings _pointerSnapshot = new();
    [ObservableProperty] private int _pointerStyleIndex;
    [ObservableProperty] private double _pointerSizePercent = 100;
    public bool CanEditPointer => CanEditTimeline && State.HasEditablePointerSources;
    public bool HasPointerDraft => State.HasEditablePointerSources &&
        (PointerStyleIndex != (int)State.PointerSettings.Style || PointerSizePercent != State.PointerSettings.SizePercent);
    public string PointerNotice => Strings[State.HasEditablePointerSources ? "ProjectPointerNotice" : "ProjectPointerUnavailable"];
    partial void OnPointerStyleIndexChanged(int value) => NotifyState();
    partial void OnPointerSizePercentChanged(double value) => NotifyState();

    private void NotifyPointers()
    {
        if (_pointerProject != State.ProjectId || _pointerSnapshot != State.PointerSettings)
        {
            _pointerProject = State.ProjectId;
            _pointerSnapshot = State.PointerSettings;
            ResetPointerDraft();
        }
        OnPropertyChanged(nameof(CanEditPointer));
        OnPropertyChanged(nameof(HasPointerDraft));
        OnPropertyChanged(nameof(PointerNotice));
    }

    private void ResetPointerDraft()
    {
        PointerStyleIndex = (int)State.PointerSettings.Style;
        PointerSizePercent = State.PointerSettings.SizePercent;
    }

    [RelayCommand]
    public Task ApplyPointerSettingsAsync()
    {
        if (!CanEditPointer) return Task.CompletedTask;
        if (!Enum.IsDefined((ProjectPointerStyle)PointerStyleIndex) || !double.IsFinite(PointerSizePercent) ||
            PointerSizePercent is < 50 or > 200 || PointerSizePercent != Math.Truncate(PointerSizePercent))
        { Error = Strings["ProjectPointerInvalid"]; return Task.CompletedTask; }
        return ExecuteAsync("SetProjectPointerSettings", new() { PointerSettings =
            new((ProjectPointerStyle)PointerStyleIndex, (int)PointerSizePercent) }, () => CanEditPointer);
    }

    [RelayCommand]
    public Task ResetPointerSettingsAsync()
    {
        if (!CanEditPointer) return Task.CompletedTask;
        PointerStyleIndex = (int)ProjectPointerStyle.Stick;
        PointerSizePercent = 100;
        return ApplyPointerSettingsAsync();
    }
}
