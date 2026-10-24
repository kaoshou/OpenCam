// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Media.Projects;

public static class PointerTrackClockVerification
{
    public static async Task<long> VerifyAsync(PointerTrackReader track, IAsyncEnumerable<long> decodedFrames,
        CancellationToken ct = default)
    {
        var verifier = new PointerClockVerifier();
        long count = 0;
        await using var frames = decodedFrames.GetAsyncEnumerator(ct);
        while (track.ReadNext() is { } item)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Frame is not { } frame) continue;
            if (!await frames.MoveNextAsync()) throw new InvalidDataException("Video has fewer frames than pointer clock.");
            verifier.Add(frame.SourcePtsMicroseconds, frames.Current);
            count++;
        }
        if (!track.Complete || await frames.MoveNextAsync())
            throw new InvalidDataException("Incomplete pointer/video frame correspondence.");
        return verifier.Finish(count, count);
    }
}
