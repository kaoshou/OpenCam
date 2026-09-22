// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Platform.Windows.Audio;

public class WindowsAudioDeviceService : IAudioDeviceService
{
    private static readonly Regex AudioDeviceRegex = new(@"\""([^\""]+)\""\s+\(audio\)", RegexOptions.Compiled);
    private static readonly Regex AlternativeNameRegex = new(@"Alternative name\s+\""([^\""]+)\""", RegexOptions.Compiled);

    public IReadOnlyList<AudioDeviceOption> GetRecordingDevices()
    {
        var devices = new List<AudioDeviceOption>();
        
        try
        {
            var ffmpegPath = FFmpegDiscovery.FindFFmpegExecutable();
            if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8, // 確保 UTF-8 符號 (例如 ®) 不會被 Windows ANSI (CP950) 誤解為亂碼
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardError.ReadToEnd();
                    process.WaitForExit(3000);

                    var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    string? currentDeviceName = null;

                    foreach (var line in lines)
                    {
                        var devMatch = AudioDeviceRegex.Match(line);
                        if (devMatch.Success)
                        {
                            currentDeviceName = devMatch.Groups[1].Value.Trim();
                            // 預設以名稱作為 ID，若緊接著有 Alternative name 則升級為純 ASCII 安全別名
                            if (!devices.Any(d => d.Name == currentDeviceName))
                            {
                                devices.Add(new AudioDeviceOption(currentDeviceName, currentDeviceName, devices.Count == 0, true));
                            }
                            continue;
                        }

                        if (currentDeviceName != null)
                        {
                            var altMatch = AlternativeNameRegex.Match(line);
                            if (altMatch.Success)
                            {
                                var altName = altMatch.Groups[1].Value.Trim();
                                var existing = devices.FirstOrDefault(d => d.Name == currentDeviceName);
                                if (existing != null)
                                {
                                    // 替換為安全別名 (可徹底杜絕特殊符號、引號或多國語言導致 FFmpeg 找不到設備崩潰)
                                    int index = devices.IndexOf(existing);
                                    devices[index] = new AudioDeviceOption(altName, currentDeviceName, existing.IsDefault, true);
                                }
                                currentDeviceName = null;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        if (devices.Count == 0)
        {
            devices.Add(new AudioDeviceOption("default_mic", "系統預設麥克風 (Default Microphone)", true, true));
        }

        return devices;
    }

    public IReadOnlyList<AudioDeviceOption> GetPlaybackDevices()
    {
        return new List<AudioDeviceOption>
        {
            new("default_system", "系統預設輸出設備 (WASAPI Loopback)", true, false)
        };
    }
}
