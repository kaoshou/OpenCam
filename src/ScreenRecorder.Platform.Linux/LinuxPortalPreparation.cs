// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Platform.Linux;

// Not exposed in UI until the complete restored-source path passes GNOME tests.
internal static class LinuxPortalPreparation
{
    internal static async Task<LinuxPortalSelection> PrepareAsync(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = ObsRuntime.TryLoad(root, "linux-x64") ?? throw new IOException("Linux capture runtime is missing or unverified.");
        var start = new ProcessStartInfo(runtime.HelperPath) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--runtime"); start.ArgumentList.Add(runtime.Root); start.ArgumentList.Add("--prepare-wayland");
        foreach (var key in new[] { "LD_PRELOAD", "LD_AUDIT", "LD_LIBRARY_PATH", "OBS_PLUGINS_PATH", "OPENCAM_LINUX_PROBE_DIAGNOSTICS" })
            start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new IOException("Linux source selection did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(125));
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, 480 * 270 * 4, process, deadline.Token);
        var events = ReadBoundedAsync(process.StandardError.BaseStream, 256 * 1024, process, deadline.Token);
        try {
            await Task.WhenAll(output, events, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Linux source selection failed; select the source again.");
            return LinuxPortalSelection.Parse(Encoding.UTF8.GetString(await events.ConfigureAwait(false)), await output.ConfigureAwait(false));
        } finally {
            // Newline is the helper's existing cancellation command. Allow OBS
            // teardown to close its own portal request/session before forced reap.
            if (!process.HasExited) {
                try {
                    await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                } catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException) {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
            }
            if (!process.HasExited)
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            try { await Task.WhenAll(output, events).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or OperationCanceledException) { /* original failure wins */ }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, Process process, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int length;
        while ((length = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0) {
            if (bytes.Length + length > maximum) {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new IOException("Linux source selection exceeded its data limit.");
            }
            bytes.Write(buffer, 0, length);
        }
        return bytes.ToArray();
    }
}
