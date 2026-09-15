using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Encoders;

public class FFmpegEncoderDetector : IEncoderDetector
{
    private readonly string _ffmpegPath;
    private readonly IFFmpegPlatformProvider _platformProvider;
    private IReadOnlyList<EncoderCapability>? _cachedCapabilities;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FFmpegEncoderDetector(IFFmpegPlatformProvider platformProvider, string? ffmpegPath = null)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _ffmpegPath = ffmpegPath ?? FFmpegDiscovery.FindFFmpegExecutable()
            ?? throw new FileNotFoundException("未在系統中探測到 FFmpeg 執行檔");
    }

    public async Task<IReadOnlyList<EncoderCapability>> DetectAvailableEncodersAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedCapabilities != null)
        {
            return _cachedCapabilities;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_cachedCapabilities != null)
            {
                return _cachedCapabilities;
            }

            var results = new List<EncoderCapability>
            {
                new(HardwareEncoderType.Auto, "auto", "自動選擇 (優先硬體加速)", true),
                new(HardwareEncoderType.SoftwareCpu, "libx264", "CPU 軟體編碼 (libx264 相容穩定)", true)
            };

            // 從 PlatformProvider 取得支援的硬體編碼器
            var probes = _platformProvider.GetHardwareEncoderProbes();
            foreach (var probe in probes)
            {
                bool isAvailable = await TestEncoderAsync(probe.EncoderName, probe.ExtraArgs, cancellationToken);
                string displayName = probe.Type switch
                {
                    HardwareEncoderType.NvidiaNvenc => "NVIDIA NVENC (硬體加速)",
                    HardwareEncoderType.IntelQsv => "Intel Quick Sync (QSV 硬體加速)",
                    HardwareEncoderType.AmdAmf => "AMD AMF (硬體加速)",
                    _ => $"{probe.EncoderName} (硬體加速)"
                };
                results.Add(new(probe.Type, probe.EncoderName, displayName, isAvailable));
            }

            _cachedCapabilities = results;
            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HardwareEncoderType> ResolveOptimalEncoderAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default)
    {
        var capabilities = await DetectAvailableEncodersAsync(cancellationToken);

        if (preferred != HardwareEncoderType.Auto)
        {
            var match = capabilities.FirstOrDefault(c => c.Type == preferred);
            if (match != null && match.IsAvailable)
            {
                return preferred;
            }
        }

        // 自動選擇策略：優先 NVENC，其次 QSV，其次 AMF，最後回退 CPU libx264
        if (capabilities.Any(c => c.Type == HardwareEncoderType.NvidiaNvenc && c.IsAvailable))
        {
            return HardwareEncoderType.NvidiaNvenc;
        }

        if (capabilities.Any(c => c.Type == HardwareEncoderType.IntelQsv && c.IsAvailable))
        {
            return HardwareEncoderType.IntelQsv;
        }

        if (capabilities.Any(c => c.Type == HardwareEncoderType.AmdAmf && c.IsAvailable))
        {
            return HardwareEncoderType.AmdAmf;
        }

        return HardwareEncoderType.SoftwareCpu;
    }

    private async Task<bool> TestEncoderAsync(string encoderName, string extraArgs, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-y -f lavfi -i testsrc=size=256x256:rate=30 -t 0.05 -c:v {encoderName} {extraArgs} -f null -",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            using var timeoutCts = new CancellationTokenSource(2500);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await proc.WaitForExitAsync(linkedCts.Token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
