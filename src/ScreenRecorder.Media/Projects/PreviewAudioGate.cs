// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Projects;

/// <summary>Transient output preference; never mutates source PCM or project edits.</summary>
public sealed class PreviewAudioGate
{
    private int _muted;
    public bool Muted { get => Volatile.Read(ref _muted) != 0; set => Volatile.Write(ref _muted, value ? 1 : 0); }
    public void Copy(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length != source.Length) throw new ArgumentException("PCM block lengths differ.");
        if (Muted) destination.Clear(); else source.CopyTo(destination);
    }
}
