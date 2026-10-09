// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

/// <summary>One recording owner shared by the home window and optional editor.</summary>
public sealed class RecordingContentController : ObservableObject
{
    private readonly Func<CancellationToken, Task> _stopPreview;
    private int _operation;
    private bool _isBusy;
    public ProjectWorkspaceViewModel Workspace { get; }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); NotifyState(); } }
    private bool Available => !IsBusy && !Workspace.IsBusy && !Workspace.StatusUnconfirmed && !Workspace.IsExporting;
    public bool CanStartNew => Available && Workspace.State.Mode is ProjectMode.Closed or ProjectMode.Ready;
    public bool CanReplaceProject => Available && Workspace.State.Mode is ProjectMode.Closed or ProjectMode.Ready or ProjectMode.Interrupted;
    public bool CanResume => Available && Workspace.State.Mode is ProjectMode.Ready or ProjectMode.Paused;
    public bool CanPause => Available && Workspace.State.Mode == ProjectMode.Recording;
    public bool CanStop => Available && Workspace.State.Mode is ProjectMode.Recording or ProjectMode.Paused or ProjectMode.SaveFailed;
    public bool CanOpenEditor => Available &&
        (Workspace.State.Mode is ProjectMode.Closed or ProjectMode.Ready or ProjectMode.Interrupted ||
         Workspace.State.Mode == ProjectMode.Paused && Workspace.State.ClipCount > 0);

    // Mandatory: callers must supply the real playback stop acknowledgement, never an implicit no-op.
    // Main-window activation remains gated until that backend is available and verified.
    public RecordingContentController(IProjectClient client, Func<CancellationToken, Task> stopPreview)
    {
        _stopPreview = stopPreview ?? throw new ArgumentNullException(nameof(stopPreview));
        Workspace = new(client);
        Workspace.StateChanged += (_, _) => NotifyState();
    }

    private void NotifyState()
    {
        foreach (var property in new[] { nameof(CanStartNew), nameof(CanReplaceProject), nameof(CanResume), nameof(CanPause), nameof(CanStop), nameof(CanOpenEditor) })
            OnPropertyChanged(property);
    }

    public Task StartNewAsync(RecordingConfiguration configuration) => !CanStartNew ? Task.CompletedTask : CaptureAsync(configuration, true);
    public Task ResumeAsync(RecordingConfiguration configuration) => !CanResume ? Task.CompletedTask : CaptureAsync(configuration, false);

    private Task CaptureAsync(RecordingConfiguration configuration, bool startNew)
    {
        // Freeze user settings at the click, not after preview shutdown or saving has awaited.
        var snapshot = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(configuration))!;
        return RunAsync(async () =>
        {
            await StopPreviewAsync();
            if (!await SaveCurrentAsync()) return;
            if (startNew) await Workspace.StartNewContentAsync(snapshot);
            else await Workspace.StartAsync(snapshot);
        });
    }

    public Task PauseAsync() => !CanPause ? Task.CompletedTask : RunAsync(Workspace.PauseAsync);
    public Task StopAsync() => !CanStop ? Task.CompletedTask : RunAsync(async () =>
    {
        await StopPreviewAsync();
        if (await SaveCurrentAsync()) await Workspace.FinishAndExportAsync();
    });
    public Task RefreshAsync() => IsBusy ? Task.CompletedTask : Workspace.RefreshAsync();

    public Task ReplaceProjectAsync(string path, string? name = null) => !CanReplaceProject ? Task.CompletedTask : RunAsync(async () =>
    {
        await StopPreviewAsync();
        // Recovery must remain explicit; do not save over a damaged primary manifest.
        if (Workspace.State.Mode != ProjectMode.Interrupted && !await SaveCurrentAsync()) return;
        if (!await Workspace.CloseAsync()) return;
        if (name is null) await Workspace.OpenAsync(path);
        else await Workspace.CreateAsync(path, name);
    });

    private async Task<bool> SaveCurrentAsync()
    {
        if (Workspace.StatusUnconfirmed) return false;
        if (Workspace.State.ProjectId is null) return true;
        if (!await Workspace.ResolveUnsavedAsync()) return false;
        return !Workspace.StatusUnconfirmed && !Workspace.HasPropertyDraft && !Workspace.State.IsDirty && Workspace.Error is null;
    }

    private async Task StopPreviewAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _stopPreview(timeout.Token).WaitAsync(timeout.Token);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref _operation, 1, 0) != 0) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { Workspace.Error = ex.Message; }
        finally { Interlocked.Exchange(ref _operation, 0); IsBusy = false; }
    }
}
