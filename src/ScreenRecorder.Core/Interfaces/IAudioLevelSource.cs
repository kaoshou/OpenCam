// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

public interface IAudioLevelSource
{
    AudioLevelSample? ReadLatestLevel(DateTimeOffset now);
}

public interface IMicrophoneLevelObserver : IAudioLevelSource, IAsyncDisposable
{
    Task<bool> StartAsync(string deviceId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
