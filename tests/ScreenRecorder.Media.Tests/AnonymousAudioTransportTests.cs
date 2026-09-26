// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO.Pipes;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;

public class AnonymousAudioTransportTests
{
    [MacOsOnlyFact]
    public async Task TwoAnonymousInputsMixAndDoNotRemainAvailableToLaterChildren()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-anonymous-mix-").FullName;
        try
        {
            using var system = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            using var microphone = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            using var systemInput = new AnonymousPipeClientStream(PipeDirection.In, system.ClientSafePipeHandle);
            using var micInput = new AnonymousPipeClientStream(PipeDirection.In, microphone.ClientSafePipeHandle);
            using var sysDescriptor = InheritedAudioDescriptor.Duplicate(systemInput)!;
            using var micDescriptor = InheritedAudioDescriptor.Duplicate(micInput)!;
            var output = Path.Combine(root, "mixed.wav");
            var info = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardInput = true,
                Arguments = $"-y -v error {sysDescriptor.Input(2)} {micDescriptor.Input(1)} -filter_complex amix=inputs=2:duration=shortest -t 0.2 -c:a pcm_s16le \"{output}\""
            };
            var stolenInput = micDescriptor.Input(1);
            using var process = Process.Start(info)!;
            sysDescriptor.Dispose();
            micDescriptor.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                var mono = new byte[19200];
                for (var i = 0; i < mono.Length / 2; i++)
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(mono.AsSpan(i * 2), (short)(6000 * Math.Sin(i * 0.1)));
                var stereo = mono.Concat(mono).ToArray();
                async Task Produce(AnonymousPipeServerStream pipe, byte[] bytes)
                {
                    try { await pipe.WriteAsync(bytes, timeout.Token); }
                    finally { pipe.Dispose(); }
                }
                await Task.WhenAll(Produce(system, stereo), Produce(microphone, mono));
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, await errors);
                var probe = await new MediaFileProbe().ProbeAsync(output, timeout.Token);
                Assert.Equal(1, probe.AudioStreamCount);
                Assert.InRange(probe.Duration.TotalSeconds, 0.19, 0.21);
                var wave = await File.ReadAllBytesAsync(output, timeout.Token);
                var offset = 12;
                while (System.Text.Encoding.ASCII.GetString(wave, offset, 4) != "data")
                {
                    var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(offset + 4));
                    offset += 8 + size + (size & 1);
                }
                var sampleBytes = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(offset + 4));
                var peak = 0;
                for (var index = offset + 8; index < offset + 8 + sampleBytes; index += 2)
                    peak = Math.Max(peak, Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(index))));
                Assert.True(peak > 1000, "Decoded mixed audio must contain the generated signal, not silence.");
                // A later unrelated child knows the descriptor number, but did not
                // inherit it: it must not receive PCM from either capture stream.
                using var stranger = Process.Start(new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
                {
                    UseShellExecute = false, RedirectStandardError = true,
                    Arguments = $"-v error {stolenInput} -t 0.1 -f null -"
                })!;
                var denied = stranger.StandardError.ReadToEndAsync(timeout.Token);
                await stranger.WaitForExitAsync(timeout.Token);
                Assert.NotEqual(0, stranger.ExitCode);
                Assert.Contains("Bad file descriptor", await denied);
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
        finally { Directory.Delete(root, true); }
    }

    [MacOsOnlyFact]
    public async Task FfmpegCanReadInheritedAnonymousAudioDescriptor()
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var info = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var arg in new[] { "-v", "error", "-f", "s16le", "-ar", "48000", "-ac", "1", "-i", "pipe:" + pipe.GetClientHandleAsString(), "-t", "0.1", "-f", "null", "-" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        pipe.DisposeLocalCopyOfClientHandle();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await pipe.WriteAsync(new byte[9600], timeout.Token);
            pipe.Dispose();
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await errors);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
