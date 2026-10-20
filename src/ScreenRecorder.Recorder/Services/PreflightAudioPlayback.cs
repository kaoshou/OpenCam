// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightAudioPlayback(Func<FileStream> openSource, Func<IProjectAudioOutput> audioFactory,
    Func<IProjectStreamingMediaProcess> processFactory, TimeSpan duration)
{
    private CancellationTokenSource? _life;
    private Task? _work;
    private string? _error;
    private Exception? _shutdownError;
    public bool Playing => _work is { IsCompleted: false };
    public string? ErrorCode => Volatile.Read(ref _error);

    public Task PlayAsync(CancellationToken ct)
    {
        if (_work is not null) throw new InvalidOperationException("Playback instances are single use.");
        _life = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _work = RunAsync(_life.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        IProjectAudioOutput? audio = null;
        try
        {
            if (duration.TotalSeconds is < 4.5 or > 60) throw new InvalidDataException("Invalid trial duration.");
            audio = audioFactory();
            await audio.StartAsync(ct);
            await using var source = openSource();
            var skip = Math.Max(0, (long)Math.Round(duration.TotalSeconds * 48000) - 5 * 48000);
            await processFactory().RunStreamingAsync(ProjectMediaJob.DecodePreflightAudio(skip, 5 * 48000),
                source, async (pcm, token) =>
                {
                    var buffer = new byte[8192];
                    long submitted = 0;
                    int count;
                    while ((count = await pcm.ReadAtLeastAsync(buffer, buffer.Length, false, token)) > 0)
                    {
                        if (count % 8 != 0) throw new InvalidDataException("Incomplete audio frame.");
                        var deadline = DateTime.UtcNow.AddSeconds(30);
                        while (submitted - Math.Max(0, audio.PositionSamples) > 4800)
                        {
                            if (DateTime.UtcNow > deadline) throw new IOException("Trial audio output stopped advancing.");
                            await Task.Delay(5, token);
                        }
                        await audio.WriteAsync(buffer.AsMemory(0, count), token);
                        submitted += count / 8;
                    }
                }, ct);
            await audio.CompleteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Audio trial playback failed");
            Volatile.Write(ref _error, "PreflightReplayFailed");
        }
        finally
        {
            if (audio is not null)
            {
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await audio.StopAsync(stop.Token);
                    await audio.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _shutdownError = ex;
                    Volatile.Write(ref _error, "PreflightReplayFailed");
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _life?.Cancel();
        if (_work is not null) await _work.WaitAsync(TimeSpan.FromSeconds(10), ct);
        if (_shutdownError is not null) throw new ProjectPreviewShutdownException(_shutdownError);
        _life?.Dispose(); _life = null;
    }
}
