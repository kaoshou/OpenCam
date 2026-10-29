// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Platform.Linux;

public sealed record LinuxAudioCatalog(IReadOnlyList<AudioDeviceOption> Microphones, IReadOnlyList<AudioDeviceOption> Playback)
{
    public string ResolveSystem(string? id) => Resolve(Playback, id, "default_system");
    public string ResolveMicrophone(string? id) => Resolve(Microphones, id, "default_mic");
    private static string Resolve(IReadOnlyList<AudioDeviceOption> devices, string? id, string alias)
    {
        var matches = devices.Where(d => string.IsNullOrWhiteSpace(id) || id is "default" || id == alias ? d.IsDefault : d.Id == id).ToArray();
        return matches.Length == 1 ? matches[0].Id : throw new IOException("The selected Linux audio endpoint is unavailable or ambiguous.");
    }
}

public sealed class LinuxDesktopCatalog : IDisplayService, IRecordingDisplayIdentityService, IAudioDeviceService
{
    public static string RuntimeRoot => Path.Combine(AppContext.BaseDirectory, "obs-runtime");
    public static string Run(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("LD_PRELOAD");
        using var process = Process.Start(start) ?? throw new IOException("Linux device query did not start.");
        async Task<string> Read(StreamReader reader) {
            var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int n;
            while ((n = await reader.ReadAsync(buffer)) > 0) {
                if (result.Length + n > 1024 * 1024) { process.Kill(true); throw new IOException("Device query exceeded its limit."); }
                result.Append(buffer, 0, n);
            }
            return result.ToString();
        }
        var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
        if (!process.WaitForExit(12000)) { process.Kill(true); process.WaitForExit(); throw new IOException("Linux device query timed out."); }
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException("Linux device query failed; check the desktop/audio session and test package dependencies.");
        return stdout.Result.Length == 0 ? stderr.Result : stdout.Result;
    }
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return OperatingSystem.IsLinux() ? EnumerateRuntime(RuntimeRoot, ObsRuntime.DefaultBackend("linux-x64")) : [];
    }
    internal static IReadOnlyList<MonitorInfo> EnumerateRuntime(string root, string backend)
    {
        var runtime = ObsRuntime.TryLoad(root, "linux-x64");
        if (runtime is null || !runtime.Backends.Contains(backend)) return [];
        try { return ParseDisplays(Run(runtime.HelperPath, "--runtime", runtime.Root, "--mode", "enumerate", "--backend", backend)); }
        catch (IOException e) { Serilog.Log.Warning(e, "Linux display enumeration unavailable"); return []; }
    }
    public static IReadOnlyList<MonitorInfo> ParseDisplays(string events)
    {
        var result = new List<MonitorInfo>();
        foreach (var line in events.Split('\n').Where(l => l.StartsWith("OCLINUX ", StringComparison.Ordinal))) {
            using var doc = JsonDocument.Parse(line[8..]); var e = doc.RootElement;
            if (e.GetProperty("event").GetString() != "display") continue;
            var id = e.GetProperty("id").GetString()!;
            if (id.StartsWith("xshm:-", StringComparison.Ordinal)) continue;
            int x, y, w, h;
            if (e.TryGetProperty("width", out var width)) {
                w = width.GetInt32(); h = e.GetProperty("height").GetInt32(); x = e.GetProperty("x").GetInt32(); y = e.GetProperty("y").GetInt32();
            } else {
                var match = Regex.Match(e.GetProperty("label").GetString() ?? "", @"\((\d+)x(\d+) @ (\d+),(\d+)\)$");
                if (!match.Success) continue;
                w = int.Parse(match.Groups[1].Value); h = int.Parse(match.Groups[2].Value);
                x = unchecked((int)uint.Parse(match.Groups[3].Value)); y = unchecked((int)uint.Parse(match.Groups[4].Value));
            }
            if (w is < 2 or > 16384 || h is < 2 or > 16384) continue;
            result.Add(new(result.Count, id, new(x, y, w, h), result.Count == 0, 1));
        }
        return result;
    }
    public static LinuxAudioCatalog ParseAudio(string sources, string sinks, string defaultInput, string defaultOutput)
    {
        using var input = JsonDocument.Parse(sources); using var output = JsonDocument.Parse(sinks);
        var microphones = new List<AudioDeviceOption>(); var playback = new List<AudioDeviceOption>();
        foreach (var value in input.RootElement.EnumerateArray()) {
            if (value.TryGetProperty("monitor_of_sink", out var monitor) && monitor.ValueKind != JsonValueKind.Null) continue;
            var id = value.GetProperty("name").GetString()!;
            microphones.Add(new(id, value.GetProperty("description").GetString() ?? id, id == defaultInput.Trim(), true));
        }
        foreach (var value in output.RootElement.EnumerateArray()) {
            var sink = value.GetProperty("name").GetString()!;
            var monitor = value.GetProperty("monitor_source").GetString()!;
            playback.Add(new(monitor, value.GetProperty("description").GetString() ?? sink, sink == defaultOutput.Trim(), false));
        }
        return new(microphones, playback);
    }
    public static LinuxAudioCatalog ReadAudio() => ParseAudio(Run("/usr/bin/pactl", "-f", "json", "list", "sources"),
        Run("/usr/bin/pactl", "-f", "json", "list", "sinks"), Run("/usr/bin/pactl", "get-default-source"), Run("/usr/bin/pactl", "get-default-sink"));
    public IReadOnlyList<AudioDeviceOption> GetRecordingDevices() { try { return ReadAudio().Microphones; } catch (IOException) { return []; } }
    public IReadOnlyList<AudioDeviceOption> GetPlaybackDevices() { try { return ReadAudio().Playback; } catch (IOException) { return []; } }
    public MonitorInfo? GetPrimaryMonitor() => GetMonitors().FirstOrDefault(m => m.IsPrimary);
    public CaptureRegion GetVirtualScreenBounds() {
        var m = GetMonitors(); if (m.Count == 0) throw new IOException("No Linux capture display is available.");
        var x = m.Min(v => v.Bounds.X); var y = m.Min(v => v.Bounds.Y);
        return new(x, y, m.Max(v => v.Bounds.X + v.Bounds.Width) - x, m.Max(v => v.Bounds.Y + v.Bounds.Height) - y);
    }
    public string? GetRecordingDisplayIdentity(MonitorInfo monitor) => GetMonitors().SingleOrDefault(m => m.DeviceName == monitor.DeviceName && m.Bounds == monitor.Bounds)?.DeviceName;
}
