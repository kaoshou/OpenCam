// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsWasapiLoopbackInputTests
{
    [Theory]
    [InlineData(48000, 2)]
    [InlineData(44100, 1)]
    [InlineData(96000, 6)]
    public void DeclaredPcmFormatLimitsProbingBeforeOpeningItsPipe(int sampleRate, int channels)
    {
        var arguments = WindowsWasapiLoopbackCapture.BuildFfmpegInputArguments(
            @"\\.\pipe\OpenCam system audio", sampleRate, channels);

        Assert.Equal($"-probesize 32 -analyzeduration 0 -f s16le -ar {sampleRate} -ac {channels} " +
            "-i \"\\\\.\\pipe\\OpenCam system audio\" ", arguments);
    }

    [Fact]
    public async Task VideoProgressesWithOneSecondOfPcmWhileAudioPipeStaysOpen()
    {
        // Removing the input probe limits must fail this test: the default PCM
        // analysis waits for substantially more data, despite its declared format.
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg), "FFmpeg must be installed for the PCM input regression.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg!)
            {
                Arguments = "-hide_banner -loglevel warning -nostats -stats_period 0.1 -progress pipe:1 " +
                    "-re -f lavfi -i testsrc2=size=64x36:rate=30 " +
                    WindowsWasapiLoopbackCapture.BuildFfmpegInputArguments("pipe:0", 48000, 2) +
                    "-map 0:v:0 -map 1:a:0 -c:v libx264 -preset ultrafast -tune zerolatency -threads 1 " +
                    "-c:a aac -t 1 -f null -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        Assert.True(process.Start());
        var firstVideoFrame = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = ReadProgressAsync(process.StandardOutput, firstVideoFrame);
        var errors = process.StandardError.ReadToEndAsync();
        var gracefulExit = false;
        var progressedWithoutEof = false;
        try
        {
            // Exactly 48,000 stereo s16le samples, then no further data and no
            // EOF. The assertion concerns prefill volume, not startup speed.
            await process.StandardInput.BaseStream.WriteAsync(new byte[48000 * 2 * 2]);
            await process.StandardInput.BaseStream.FlushAsync();
            try
            {
                await firstVideoFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
                progressedWithoutEof = true;
            }
            catch (TimeoutException) { }
        }
        finally
        {
            // EOF allows both the failing old probe and the successful bounded
            // probe to finish cleanly; kill only if the cleanup itself hangs.
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                gracefulExit = true;
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await progress;
        }

        var stderr = await errors;
        Assert.True(gracefulExit, "FFmpeg required forced termination during cleanup. " + stderr);
        Assert.Equal(0, process.ExitCode);
        Assert.True(progressedWithoutEof,
            "FFmpeg required more than one second of PCM, or EOF, before reporting video progress. " + stderr);
    }

    private static async Task ReadProgressAsync(StreamReader reader, TaskCompletionSource<int> firstVideoFrame)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("frame=", StringComparison.Ordinal) &&
                int.TryParse(line.AsSpan(6), out var frame) && frame > 0)
                firstVideoFrame.TrySetResult(frame);
        }
    }
}
