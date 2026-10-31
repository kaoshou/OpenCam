// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Linux;

public sealed record LinuxPortalSelectionView(string SourceId, int Width, int Height, int PreviewWidth, int PreviewHeight, string PreviewRgba);

// Process-local registry. UI receives only the view above, never restoration
// credentials. The Recorder process owns the actual single-use token chain.
public sealed class LinuxPortalSources
{
    public static LinuxPortalSources Shared { get; } = new();
    private readonly object gate = new();
    private LinuxPortalSelectionView? current;
    private string? token;
    private RestoreGrant? active;

    public MonitorInfo? Monitor {
        get { lock (gate) return current is { } c ? new(0, c.SourceId, new(0, 0, c.Width, c.Height), false, 1) : null; }
    }
    public async Task<LinuxPortalSelectionView> PrepareAsync(string root, CancellationToken ct)
    {
        lock (gate) if (active is not null) throw new IOException("Stop recording before selecting another source.");
        return Store(await LinuxPortalPreparation.PrepareAsync(root, ct).ConfigureAwait(false));
    }
    internal LinuxPortalSelectionView Store(LinuxPortalSelection selection)
    {
        lock (gate) {
            if (active is not null) throw new IOException("Stop recording before selecting another source.");
            var view = new LinuxPortalSelectionView(selection.SourceId, selection.Width, selection.Height,
                selection.PreviewWidth, selection.PreviewHeight, Convert.ToBase64String(selection.Rgba));
            current = view; token = selection.RestoreToken;
            return view;
        }
    }
    internal RestoreGrant BeginRestore(string sourceId, int width, int height)
    {
        lock (gate) {
            if (active is not null || token is null || current is not { } c ||
                c.SourceId != sourceId || c.Width != width || c.Height != height)
                throw new IOException("The Wayland source needs to be selected again before recording.");
            active = new(this, c, token);
            token = null; // A failed attempt must not retry a consumed credential.
            return active;
        }
    }
    internal sealed class RestoreGrant(LinuxPortalSources owner, LinuxPortalSelectionView source, string token) : IDisposable
    {
        internal string Token { get; } = token;
        private bool seen, disposed;
        internal void Rotate(string id, int width, int height, string nextToken)
        {
            lock (owner.gate) {
                if (disposed || seen || !ReferenceEquals(owner.active, this)) throw new IOException("Invalid Wayland grant lifecycle.");
                seen = true;
                if (id != source.SourceId || width != source.Width || height != source.Height ||
                    string.IsNullOrEmpty(nextToken) || System.Text.Encoding.UTF8.GetByteCount(nextToken) > 4096 ||
                    nextToken.Any(c => c < 32 || c == 127))
                    throw new IOException("The restored Wayland source did not match the authorized selection.");
                owner.token = nextToken;
            }
        }
        public void Dispose()
        {
            lock (owner.gate) {
                if (disposed) return;
                disposed = true;
                if (ReferenceEquals(owner.active, this)) owner.active = null;
            }
        }
        public override string ToString() => "Wayland restore grant (redacted)";
    }
}
