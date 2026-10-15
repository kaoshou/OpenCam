// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Recording;

public static class RecordingCountdown
{
    public static async Task RunAsync(int seconds, Action<int> show, CancellationToken ct, TimeProvider? time = null)
    {
        if (seconds is not (0 or 3 or 5)) throw new ArgumentOutOfRangeException(nameof(seconds));
        ct.ThrowIfCancellationRequested();
        time ??= TimeProvider.System;
        var start = time.GetTimestamp(); var last = -1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = seconds - time.GetElapsedTime(start).TotalSeconds;
            if (remaining <= 0) break;
            var number = (int)Math.Ceiling(remaining);
            if (number != last) { last = number; show(number); }
            ct.ThrowIfCancellationRequested();
            if (time.GetElapsedTime(start).TotalSeconds >= seconds) break;
            await Task.Delay(40, ct);
        }
        ct.ThrowIfCancellationRequested();
    }
}
