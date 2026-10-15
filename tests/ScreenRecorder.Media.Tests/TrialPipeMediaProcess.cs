// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Projects;
using System.Diagnostics;
using System.Text;

namespace ScreenRecorder.Media.Tests;

/// <summary>Real FFmpeg stdin replay of small synthetic MKVs, for portable trial tests only.</summary>
internal sealed class TrialPipeMediaProcess(string executable) : IProjectStreamingMediaProcess
{
    public async Task RunStreamingAsync(ProjectMediaJob job, FileStream input,
        Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
    {
        // Only the test transport changes. The real preview job, filters, decoder,
        // frame/audio lengths, project hash checks and cancellation remain intact.
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in job.FileDescriptorArguments())
            start.ArgumentList.Add(argument == "fd:" ? "pipe:0" : argument);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var token = lifetime.Token;
        input.Position = 0;
        using var child = Process.Start(start) ?? throw new IOException("Cannot start trial test decoder.");
        var diagnostics = new StringBuilder();
        async Task Guard(Func<Task> work)
        { try { await work(); } catch { lifetime.Cancel(); throw; } }
        async Task Feed()
        {
            try { await input.CopyToAsync(child.StandardInput.BaseStream, token); }
            finally { child.StandardInput.Close(); }
        }
        async Task Read()
        {
            await consume(child.StandardOutput.BaseStream, token);
            if (await child.StandardOutput.BaseStream.ReadAsync(new byte[1], token) != 0)
                throw new InvalidDataException("Trial decoder returned excess output.");
        }
        async Task Errors()
        {
            var buffer = new char[1024];
            int count;
            while ((count = await child.StandardError.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                if (diagnostics.Length + count > 32768) throw new InvalidDataException("Trial diagnostic quota exceeded.");
                diagnostics.Append(buffer, 0, count);
            }
        }
        var tasks = Task.WhenAll(Guard(Feed), Guard(Read), Guard(Errors), child.WaitForExitAsync(token));
        try
        {
            await tasks.WaitAsync(token);
            if (child.ExitCode != 0) throw new InvalidDataException($"Trial decoder exited {child.ExitCode}: {diagnostics}");
        }
        finally
        {
            lifetime.Cancel();
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            try { await tasks; } catch (Exception ex) { Debug.WriteLine(ex); }
        }
    }
}
