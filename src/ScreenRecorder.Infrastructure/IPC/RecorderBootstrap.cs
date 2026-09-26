// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace ScreenRecorder.Infrastructure.IPC;

public static class RecorderBootstrap
{
    public static async Task<(int ParentPid, byte[] Key)> ReadAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--parent-pid");
        if (index < 0 || index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], out var pid) || pid <= 0 || pid == Environment.ProcessId ||
            !Console.IsInputRedirected)
            throw new InvalidDataException("Recorder requires a UI bootstrap channel.");

        using var parent = Process.GetProcessById(pid);
        var expected = Environment.ProcessPath;
        var actual = parent.MainModule?.FileName;
        if (expected == null || actual == null || parent.HasExited ||
            !string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            string.Equals(Path.GetFileNameWithoutExtension(expected), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recorder requires the matching OpenCam apphost.");

        // stdin is a private inherited OS pipe. The daemon additionally requires every
        // named-pipe peer to have this exact parent's PID, so a forged bootstrap is insufficient.
        var key = new byte[AuthenticatedIpc.KeySize];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var input = Console.OpenStandardInput();
        await input.ReadExactlyAsync(key, timeout.Token);
        if (parent.HasExited) throw new InvalidDataException("Recorder parent exited.");
        return (pid, key);
    }
}
