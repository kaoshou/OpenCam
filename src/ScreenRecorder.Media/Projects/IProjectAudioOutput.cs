// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Projects;

/// <summary>48kHz stereo interleaved float PCM. Stop acknowledges device release, not only UI state.</summary>
public interface IProjectAudioOutput : IAsyncDisposable
{
    long PositionSamples { get; }
    Task StartAsync(CancellationToken ct);
    Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct);
    Task CompleteAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
