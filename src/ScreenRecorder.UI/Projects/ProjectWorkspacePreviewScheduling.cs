// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private bool _previewPending, _drainingPreview;
    public event EventHandler? PreviewRequested;
    public void RequestPreviewRefresh()
    {
        _previewPending = true;
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }
    public async Task DrainPreviewRequestsAsync()
    {
        if (_drainingPreview) return;
        _drainingPreview = true;
        try
        {
            while (_previewPending && _pollingPreview == 0 && !IsPlayingPreview)
            {
                _previewPending = false;
                await PollPreviewAsync();
            }
        }
        finally { _drainingPreview = false; }
    }
    private void PreviewSlotReleased()
    {
        Interlocked.Exchange(ref _pollingPreview, 0);
        if (_previewPending) PreviewRequested?.Invoke(this, EventArgs.Empty);
    }
}
