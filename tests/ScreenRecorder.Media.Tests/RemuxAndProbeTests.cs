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

    [Fact]
    public async Task Concat_WhenAudioChangesAfterPause_PreservesLaterAudio()
    {
        var ffmpegPath = FFmpegDiscovery.FindFFmpegExecutable();
        var ffprobePath = FFmpegDiscovery.FindFFprobeExecutable();

        Assert.NotNull(ffmpegPath);
        Assert.NotNull(ffprobePath);

        var silentSegment = Path.Combine(_tempDir, "silent.mkv");
        var audibleSegment = Path.Combine(_tempDir, "audible.mkv");
        var outputPath = Path.Combine(_tempDir, "paused-audio-change.mp4");

        await RunFfmpegAsync(
            ffmpegPath!,
            $"-y -f lavfi -i color=c=black:size=320x240:rate=30 " +
            "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 " +
            "-t 0.8 -shortest -c:v libx264 -pix_fmt yuv420p " +
            $"-c:a aac -ar 48000 -ac 2 \"{silentSegment}\"");
        await RunFfmpegAsync(
            ffmpegPath!,
            $"-y -f lavfi -i color=c=black:size=320x240:rate=30 " +
            "-f lavfi -i sine=frequency=1000:sample_rate=48000 " +
            "-t 0.8 -shortest -c:v libx264 -pix_fmt yuv420p " +
            $"-c:a aac -ar 48000 -ac 2 \"{audibleSegment}\"");

        var remuxer = new StreamCopyRemuxer(ffmpegPath);
        var success = await remuxer.ConcatAndRemuxToMp4Async(
            new[] { silentSegment, audibleSegment },
            outputPath);

        Assert.True(success);
        var probe = await new MediaFileProbe(ffprobePath).ProbeAsync(outputPath);
        Assert.True(probe.IsValid);
        Assert.Equal(1, probe.AudioStreamCount);
        Assert.True(probe.Duration.TotalSeconds > 1.3);

        var tailPcm = await DecodeAudioTailAsync(ffmpegPath!, outputPath);
        Assert.True(tailPcm.Length > 0);
        Assert.True(
            tailPcm.Chunk(2)
                .Where(bytes => bytes.Length == 2)
                .Select(bytes => Math.Abs((int)BitConverter.ToInt16(bytes, 0)))
                .Any(sample => sample > 500),
            "Audio enabled after pause must remain audible in the final MP4.");
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stderrTask = process!.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        await stdoutTask;
        Assert.True(process.ExitCode == 0, stderr);
    }

    private static async Task<byte[]> DecodeAudioTailAsync(
        string ffmpegPath,
        string inputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments =
                $"-v error -ss 0.9 -i \"{inputPath}\" -map 0:a:0 " +
                "-t 0.4 -f s16le -acodec pcm_s16le -",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        await using var output = new MemoryStream();
        var copyTask = process!.StandardOutput.BaseStream.CopyToAsync(output);
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await copyTask;
        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, stderr);
        return output.ToArray();
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
