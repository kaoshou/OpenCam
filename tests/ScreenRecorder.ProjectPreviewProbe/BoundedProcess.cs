// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;

namespace ScreenRecorder.ProjectPreviewProbe;

internal static class BoundedProcess
{
    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        Stream? input, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, RedirectStandardInput = input is not null,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Media process did not start.");
        using var cancel = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stdout = ReadBoundedAsync(process.StandardOutput, token);
        var stderr = ReadBoundedAsync(process.StandardError, token);
        var feed = FeedAsync();
        try
        {
            await Task.WhenAll(stdout, stderr, feed, process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException($"Media process exit {process.ExitCode}: {await stderr}");
            return await stdout;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        async Task FeedAsync()
        {
            if (input is null) return;
            try { await input.CopyToAsync(process.StandardInput.BaseStream, 65536, token); }
            catch (IOException) { /* A one-frame decoder may finish before consuming the whole source. Exit status still checked. */ }
            finally { process.StandardInput.Close(); }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + count > 1024 * 1024) throw new InvalidDataException("Media output limit exceeded.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
