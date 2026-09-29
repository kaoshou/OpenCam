// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace ScreenRecorder.Media.Encoders;

public enum EncoderProbeStatus { Available, ProbeFailed, TimedOut, Cancelled, StartFailed }

public sealed record EncoderProbeResult(EncoderProbeStatus Status, int? ExitCode,
    TimeSpan Elapsed, string StdoutTail, string StderrTail, bool CleanupCompleted);

public interface IEncoderProbeRunner
{
    Task<EncoderProbeResult> RunAsync(ProcessStartInfo startInfo, TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);
}

public static class EncoderProbePolicy
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DetectionBudget = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SuccessCacheLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FailureCacheLifetime = TimeSpan.FromSeconds(30);
    public const int TailCharacterLimit = 4096;
}
