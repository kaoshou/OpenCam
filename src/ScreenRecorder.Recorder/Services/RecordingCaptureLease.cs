// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Recorder.Services;

public sealed class RecordingCaptureLease
{
    private readonly object _gate = new();
    private Token? _active;
    public bool IsOwned { get { lock (_gate) return _active is not null; } }
    public ValueTask<IAsyncDisposable> AcquireAsync(Guid owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (owner == Guid.Empty) throw new ArgumentException("Capture owner is required.", nameof(owner));
        lock (_gate)
        {
            if (_active is not null) throw new InvalidOperationException("Capture is already owned; stop the current capture first.");
            _active = new(this);
            return ValueTask.FromResult<IAsyncDisposable>(_active);
        }
    }

    private sealed class Token(RecordingCaptureLease parent) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (parent._gate)
                if (ReferenceEquals(parent._active, this)) parent._active = null;
            return ValueTask.CompletedTask;
        }
    }
}
