// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.ProjectPreviewProbe;

/// <summary>Diagnostic stereo 48kHz PCM streaming; no audio device is opened here.</summary>
internal static class StreamingAudioDecoder
{
    internal static Task DecodeAsync(string ffmpeg, FileStream source, decimal sourceOriginSeconds, int sampleCount,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> publish, CancellationToken ct)
    {
        if (sampleCount <= 0 || sampleCount > 48000 * 10) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        ArgumentNullException.ThrowIfNull(publish);
        var filter = FormattableString.Invariant($"atrim=start={sourceOriginSeconds},asetpts=PTS-({sourceOriginSeconds})/TB,aresample=48000:first_pts=0,apad=whole_len={sampleCount},atrim=end_sample={sampleCount}");
        string[] args = ["-nostdin", "-hide_banner", "-loglevel", "quiet", "-copyts", "-protocol_whitelist", "fd,pipe",
            "-i", "fd:", "-map", "0:a:0", "-vn", "-af", filter, "-ac", "2", "-ar", "48000",
            "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"];
        return MacFileDescriptorProcess.RunStreamingAsync(ffmpeg, args, source, async (pcm, diagnostics, token, abort) =>
        {
            var audio = ReadAudio();
            var errors = RejectDiagnostics();
            await MacFileDescriptorProcess.JoinReadersAsync(audio, errors, abort);
            return true;

            async Task ReadAudio()
            {
                var remaining = checked(sampleCount * 8);
                var buffer = new byte[1024 * 8];
                while (remaining > 0)
                {
                    var length = Math.Min(remaining, buffer.Length);
                    await pcm.ReadExactlyAsync(buffer.AsMemory(0, length), token);
                    for (var i = 0; i < length; i += 4)
                        if (!float.IsFinite(BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i, 4)))))
                            throw new InvalidDataException("Non-finite PCM sample");
                    token.ThrowIfCancellationRequested();
                    // Consumer owns data only until its awaited callback returns. No unbounded audio queue.
                    await publish(buffer.AsMemory(0, length), token);
                    remaining -= length;
                }
                if (await pcm.ReadAsync(buffer.AsMemory(0, 1), token) != 0)
                    throw new InvalidDataException("PCM exceeds requested interval");
            }
            async Task RejectDiagnostics()
            {
                var bytes = new byte[4096];
                var total = 0;
                int read;
                while ((read = await diagnostics.ReadAsync(bytes, token)) != 0)
                {
                    total += read;
                    if (total > 1024 * 1024) throw new InvalidDataException("Diagnostic quota exceeded");
                }
            }
        }, ct);
    }
}
