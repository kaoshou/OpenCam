// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Platform.Windows;

/// <summary>Output-only WASAPI renderer. A bounded queue applies backpressure to FFmpeg.</summary>
public sealed class WindowsProjectAudioOutput : IProjectAudioOutput
{
    private BlockingCollection<byte[]>? queue;
    private CancellationTokenSource? stop;
    private Task? worker;
    private Exception? failure;
    private long position = -1;
    public long PositionSamples => Interlocked.Read(ref position);

    public async Task StartAsync(CancellationToken ct)
    {
        await StopAsync(ct);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ct.ThrowIfCancellationRequested();
        queue = new(16);
        stop = new();
        failure = null;
        Interlocked.Exchange(ref position, -1);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The device and its COM interfaces are created, used and released on the same worker.
        worker = Task.Factory.StartNew(() => Render(ready, queue, stop.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch { await StopAsync(CancellationToken.None); throw; }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
    {
        if (pcm.Length == 0 || pcm.Length > 8192 || pcm.Length % 8 != 0)
            throw new InvalidDataException("Invalid stereo float PCM block.");
        var target = queue ?? throw new InvalidOperationException("Audio is not started.");
        var owned = pcm.ToArray();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (failure is not null) throw new IOException("Preview audio output failed.", failure);
            if (worker!.IsCompleted || target.IsAddingCompleted || stop!.IsCancellationRequested)
                throw new InvalidOperationException("Audio output is stopped.");
            if (target.TryAdd(owned)) return;
            await Task.Delay(5, ct);
        }
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        var target = queue ?? throw new InvalidOperationException("Audio is not started.");
        target.CompleteAdding();
        await worker!.WaitAsync(TimeSpan.FromSeconds(5), ct);
        if (failure is not null) throw new IOException("Preview audio did not complete.", failure);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (worker is null) return;
        stop!.Cancel();
        // No success acknowledgement until the renderer releases the actual device.
        await worker.WaitAsync(TimeSpan.FromSeconds(3), ct);
        queue!.Dispose(); stop.Dispose();
        queue = null; stop = null; worker = null;
    }

    private void Render(TaskCompletionSource ready, BlockingCollection<byte[]> input, CancellationToken ct)
    {
        AudioClient? device = null;
        AudioRenderClient? renderer = null;
        MMDevice? endpoint = null;
        MMDeviceEnumerator? enumerator = null;
        var started = false;
        try
        {
            enumerator = new MMDeviceEnumerator();
            endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            // Optional endpoint metadata must never prevent an otherwise usable
            // output device from playing (some drivers omit volume interfaces).
            try
            {
                Log.Information("Project preview Windows output: {Device}, muted {Muted}, volume {Volume}",
                    endpoint.FriendlyName, endpoint.AudioEndpointVolume.Mute, endpoint.AudioEndpointVolume.MasterVolumeLevelScalar);
            }
            catch (Exception ex) { Log.Debug(ex, "Project preview output metadata unavailable"); }
            device = endpoint.AudioClient;
            // Shared-mode conversion permits 48k float stereo even when endpoint mix format differs.
            device.Initialize(AudioClientShareMode.Shared,
                (AudioClientStreamFlags)0x88000000, TimeSpan.TicksPerSecond / 10, 0,
                WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), Guid.Empty);
            renderer = device.AudioRenderClient;
            ready.TrySetResult();
            byte[]? pending = null;
            var offset = 0;
            long submitted = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var padding = device.CurrentPadding;
                // Count only submitted PCM consumed by the endpoint, not free-running wall time.
                // During a decoder gap the position stays put instead of inventing silence samples.
                if (started) Interlocked.Exchange(ref position, Math.Max(0, submitted - padding));
                if (pending is null && input.TryTake(out var next)) { pending = next; offset = 0; }
                if (pending is null && input.IsCompleted && padding == 0) break;
                var available = device.BufferSize - padding;
                if (pending is not null && available > 0)
                {
                    var frames = Math.Min(available, (pending.Length - offset) / 8);
                    var buffer = renderer.GetBuffer(frames);
                    try { Marshal.Copy(pending, offset, buffer, frames * 8); }
                    finally { renderer.ReleaseBuffer(frames, AudioClientBufferFlags.None); }
                    submitted += frames; offset += frames * 8;
                    if (offset == pending.Length) pending = null;
                    if (!started) { device.Start(); started = true; Interlocked.Exchange(ref position, 0); }
                }
                else if (ct.WaitHandle.WaitOne(5)) ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { ready.TrySetCanceled(ct); }
        catch (Exception ex) { failure = ex; Log.Warning(ex, "Project preview Windows audio output failed"); ready.TrySetException(ex); }
        finally
        {
            // Cleanup failures fault the worker: Stop must not falsely acknowledge silence.
            try { if (started) { device!.Stop(); device.Reset(); } }
            finally { renderer?.Dispose(); device?.Dispose(); endpoint?.Dispose(); enumerator?.Dispose(); }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
