// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.Encoders;

namespace ScreenRecorder.Media.Tests;

public class EncoderProbeRunnerTests
{
    internal static ProcessStartInfo Child(string mode)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "probe-child", "ScreenRecorder.TestChild.dll"));
        info.ArgumentList.Add(mode);
        return info;
    }

    [Fact]
    public async Task SuccessAndFailure_ReportExitCode()
    {
        var runner = new EncoderProbeRunner();
        var success = await runner.RunAsync(Child("success"), TimeSpan.FromSeconds(3));
        var failed = await runner.RunAsync(Child("exit-7"), TimeSpan.FromSeconds(3));
        Assert.Equal(EncoderProbeStatus.Available, success.Status);
        Assert.Equal(0, success.ExitCode);
        Assert.Equal(EncoderProbeStatus.ProbeFailed, failed.Status);
        Assert.Equal(7, failed.ExitCode);
        Assert.Contains("encoder failed", failed.StderrTail);
        Assert.True(success.CleanupCompleted);
    }

    [Fact]
    public async Task FloodWithoutNewlines_IsBoundedAndDrained()
    {
        var result = await new EncoderProbeRunner().RunAsync(Child("flood"), TimeSpan.FromSeconds(3));
        Assert.Equal(EncoderProbeStatus.Available, result.Status);
        Assert.InRange(result.StdoutTail.Length, 1, 4096);
        Assert.InRange(result.StderrTail.Length, 1, 4096);
        Assert.EndsWith("stdout-end", result.StdoutTail);
        Assert.EndsWith("stderr-end", result.StderrTail);
    }

    [Fact]
    public async Task SlowInitialization_WithinBudgetSucceeds()
    {
        var result = await new EncoderProbeRunner().RunAsync(Child("delay"), TimeSpan.FromSeconds(3));
        Assert.Equal(EncoderProbeStatus.Available, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndCancellation_ReapOwnedProcess(bool cancel)
    {
        using var cts = new CancellationTokenSource();
        if (cancel) cts.CancelAfter(500);
        var result = await new EncoderProbeRunner().RunAsync(Child("wait"),
            TimeSpan.FromMilliseconds(cancel ? 3000 : 500), cts.Token);
        Assert.Equal(cancel ? EncoderProbeStatus.Cancelled : EncoderProbeStatus.TimedOut, result.Status);
        Assert.True(result.CleanupCompleted);
        AssertChildExited(result.StdoutTail);
    }

    [Fact]
    public async Task Timeout_LeavesUnrelatedProcessAlive()
    {
        using var unrelated = Process.Start(Child("wait"))!;
        try
        {
            Assert.StartsWith("PID:", await unrelated.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var result = await new EncoderProbeRunner().RunAsync(Child("wait"), TimeSpan.FromMilliseconds(500));
            Assert.Equal(EncoderProbeStatus.TimedOut, result.Status);
            Assert.False(unrelated.HasExited);
            AssertChildExited(result.StdoutTail);
        }
        finally
        {
            if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
            await unrelated.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task MissingExecutable_IsStartFailed()
    {
        var info = Child("success");
        info.FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = await new EncoderProbeRunner().RunAsync(info, TimeSpan.FromSeconds(3));
        Assert.Equal(EncoderProbeStatus.StartFailed, result.Status);
        Assert.Null(result.ExitCode);
        Assert.True(result.CleanupCompleted);
    }

    [Fact]
    public async Task RepeatedTimeouts_DoNotAccumulateChildren()
    {
        for (var i = 0; i < 10; i++)
        {
            var result = await new EncoderProbeRunner().RunAsync(Child("wait"), TimeSpan.FromMilliseconds(500));
            Assert.Equal(EncoderProbeStatus.TimedOut, result.Status);
            Assert.True(result.CleanupCompleted);
            AssertChildExited(result.StdoutTail);
        }
    }

    private static void AssertChildExited(string output)
    {
        var line = output.Split('\n').First(x => x.StartsWith("PID:"));
        var pid = int.Parse(line[4..]);
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited, $"probe child {pid} survived cleanup");
        }
        catch (ArgumentException) { /* PID no longer exists. */ }
    }
}
