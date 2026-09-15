using System.Diagnostics;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class RemuxAndProbeTests : IDisposable
{
    private readonly string _tempDir;

    public RemuxAndProbeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MediaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StreamCopyRemuxer_And_Probe_EndToEndIntegration()
    {
        var ffmpegPath = FFmpegDiscovery.FindFFmpegExecutable();
        var ffprobePath = FFmpegDiscovery.FindFFprobeExecutable();

        Assert.NotNull(ffmpegPath);
        Assert.NotNull(ffprobePath);

        var mkvPath = Path.Combine(_tempDir, "source.mkv");
        var mp4Path = Path.Combine(_tempDir, "target.mp4");

        // 1. 利用 lavfi testsrc 快速生成 1 秒的合法 H.264 測試 MKV 檔
        var genArgs = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{mkvPath}\"";
        var genPsi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = genArgs,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var genProc = Process.Start(genPsi))
        {
            Assert.NotNull(genProc);
            await genProc!.WaitForExitAsync();
            Assert.Equal(0, genProc.ExitCode);
        }
        Assert.True(File.Exists(mkvPath));

        // 2. 測試 StreamCopyRemuxer (-c copy) 無損轉為 MP4
        var remuxer = new StreamCopyRemuxer(ffmpegPath);
        var success = await remuxer.RemuxToMp4Async(mkvPath, mp4Path);

        Assert.True(success);
        Assert.True(File.Exists(mp4Path));
        // 原始 MKV 必須被完整保留
        Assert.True(File.Exists(mkvPath));

        // 3. 測試 MediaFileProbe 驗證生成的 MP4 檔案
        var probe = new MediaFileProbe(ffprobePath);
        var probeResult = await probe.ProbeAsync(mp4Path);

        Assert.True(probeResult.IsValid);
        Assert.Equal(1, probeResult.VideoStreamCount);
        Assert.Equal(320, probeResult.Width);
        Assert.Equal(240, probeResult.Height);
        Assert.True(probeResult.Duration.TotalSeconds > 0.5);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }
}
