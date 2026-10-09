// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Platform.macOS;

/// <summary>Owns exactly one bundled PCM output helper. Never opens capture devices.</summary>
public sealed class MacProjectAudioOutput(string helperPath) : IProjectAudioOutput
{
    private Process? _child;
    private Task? _clockReader, _errorReader;
    private CancellationTokenSource? _lifetime;
    private long _position = -1;
    private Exception? _failure;
    public long PositionSamples => Interlocked.Read(ref _position);
    public bool IsRunning => _child is { HasExited: false };

    public async Task StartAsync(CancellationToken ct)
    {
        await StopAsync(ct);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(helperPath) || !File.Exists(helperPath))
            throw new FileNotFoundException("Bundled project audio helper is unavailable.");
        ct.ThrowIfCancellationRequested();
        _position = -1;
        _failure = null;
        var start = new ProcessStartInfo(helperPath) { UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        _child = Process.Start(start) ?? throw new IOException("Preview audio helper did not start.");
        _lifetime = new();
        _clockReader = ReadClockAsync(_child.StandardOutput, _lifetime.Token);
        _errorReader = DrainErrorsAsync(_child.StandardError, _lifetime.Token);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
    {
        if (pcm.Length == 0 || pcm.Length > 8192 || pcm.Length % 8 != 0)
            throw new InvalidDataException("Invalid stereo PCM block.");
        if (_failure is not null) throw new IOException("Preview audio failed.", _failure);
        var child = _child ?? throw new InvalidOperationException("Audio is not started.");
        if (child.HasExited) throw new IOException("Preview audio output stopped unexpectedly.");
        await child.StandardInput.BaseStream.WriteAsync(pcm, ct);
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        var child = _child ?? throw new InvalidOperationException("Audio is not started.");
        child.StandardInput.Close();
        await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        await Task.WhenAll(_clockReader!, _errorReader!);
        if (child.ExitCode != 0 || _failure is not null)
            throw new IOException("Preview audio did not finish successfully.", _failure);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        var child = _child;
        if (child is null) return;
        // Killing this dedicated helper discards all queued buffers; exit is the stop acknowledgement.
        // Never return success on a timeout or dispose the process before confirming its exit.
        if (!child.HasExited) child.Kill();
        await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
        _lifetime!.Cancel();
        try { await Task.WhenAll(_clockReader!, _errorReader!); } catch (OperationCanceledException) { }
        child.Dispose();
        _lifetime.Dispose();
        _lifetime = null;
        _child = null;
    }

    private async Task ReadClockAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            var line = new StringBuilder();
            var buffer = new char[512];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != '\n')
                    {
                        if (line.Length >= 4096) throw new InvalidDataException("Audio clock quota exceeded.");
                        line.Append(buffer[i]);
                        continue;
                    }
                    using var json = JsonDocument.Parse(line.ToString());
                    var value = json.RootElement.GetProperty("sampleClock").GetInt64();
                    if (value < 0 || value < PositionSamples) throw new InvalidDataException("Audio clock moved backwards.");
                    Interlocked.Exchange(ref _position, value);
                    line.Clear();
                }
            if (line.Length > 0) throw new InvalidDataException("Truncated audio clock packet.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _failure = ex; }
    }

    private async Task DrainErrorsAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            var chars = new char[4096];
            var total = 0;
            int read;
            while ((read = await reader.ReadAsync(chars.AsMemory(), ct)) != 0)
                if ((total += read) > 1024 * 1024) throw new InvalidDataException("Audio diagnostic quota exceeded.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _failure = ex; }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
