// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.Media.Capture;

/// <summary>Fail-closed deployment validation; merely finding native libraries never enables capture.</summary>
public sealed class ObsRuntime
{
    public const string PinnedSourceRevision = "72e86655715d9a25d69ce446aa4dd44ba58e10d2";
    public string Root { get; }
    public string HelperPath { get; }
    public string Platform { get; }
    public IReadOnlySet<string> Backends { get; }
    public IReadOnlySet<string> AudioModes { get; }
    public IReadOnlySet<string> Encoders { get; }

    private ObsRuntime(string root, string platform, HashSet<string> backends, HashSet<string> audio, HashSet<string> encoders)
    {
        Root = root;
        Platform = platform;
        HelperPath = platform == "win-x64" ? Path.Combine(root, "OpenCam.ObsCapture.exe")
            : Path.Combine(root, "Contents", "MacOS", "OpenCam.ObsCapture");
        Backends = backends;
        AudioModes = audio;
        Encoders = encoders;
    }

    public static string? CurrentPlatform => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? "win-x64" : OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : null;

    public static ObsRuntime? TryLoad(string root, string? platform = null)
    {
        platform ??= CurrentPlatform;
        if (platform is not ("win-x64" or "osx-arm64")) return null;
        try
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root) || HasReparsePoint(root, root)) return null;
            var manifest = Path.Combine(root, "runtime.json");
            if (HasReparsePoint(manifest, root) || new FileInfo(manifest).Length > 262144) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifest));
            var value = json.RootElement;
            if (!value.GetProperty("enabled").GetBoolean() || value.GetProperty("protocol").GetInt32() != 1 ||
                value.GetProperty("obsVersion").GetString() != "33.0.0" || value.GetProperty("platform").GetString() != platform ||
                value.GetProperty("sourceRevision").GetString() != PinnedSourceRevision) return null;
            var capabilities = value.GetProperty("capabilities");
            var backends = ReadSet(capabilities, "backends", ["wgc", "dxgi", "sck", "synthetic"]);
            var audio = ReadSet(capabilities, "audio", ["none", "system", "microphone", "both", "silence"]);
            var encoders = ReadSet(capabilities, "encoders", ["cpu", "nvenc", "qsv", "amf", "apple"]);
            var files = value.GetProperty("files");
            if (files.ValueKind != JsonValueKind.Object) return null;
            var listed = new HashSet<string>(platform == "win-x64" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var entry in files.EnumerateObject())
            {
                if (!ValidRelativePath(entry.Name) || !listed.Add(entry.Name)) return null;
                var digest = entry.Value.GetString();
                if (digest?.Length != 64 || !digest.All(Uri.IsHexDigit)) return null;
                var path = Path.Combine(root, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || HasReparsePoint(path, root)) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), digest, StringComparison.OrdinalIgnoreCase)) return null;
            }
            var runtime = new ObsRuntime(root, platform, backends, audio, encoders);
            var helper = Path.GetRelativePath(root, runtime.HelperPath).Replace(Path.DirectorySeparatorChar, '/');
            if (!listed.Contains(helper)) return null;
            // Inspect directories before walking them: never follow a symlink into an unrelated tree.
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (HasReparsePoint(entry, root)) return null;
                    if (Directory.Exists(entry)) pending.Push(entry);
                    else if (entry != manifest && !listed.Contains(Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/')))
                        return null;
                }
            }
            return runtime;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static HashSet<string> ReadSet(JsonElement parent, string name, string[] allowed)
    {
        var values = parent.GetProperty(name);
        if (values.ValueKind != JsonValueKind.Array) throw new JsonException();
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values.EnumerateArray())
        {
            var value = item.GetString();
            if (value is null || !allowed.Contains(value) || !result.Add(value)) throw new JsonException();
        }
        if (result.Count == 0) throw new JsonException();
        return result;
    }

    private static bool ValidRelativePath(string path) => path.Length is > 0 and < 4096 &&
        !path.Contains('\\') && !path.Contains(':') && !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part is not ("" or "." or "..") && part.All(character => !char.IsControl(character)));

    private static bool HasReparsePoint(string path, string root)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            // Parent OS aliases (e.g. macOS /var -> /private/var) are outside the runtime inventory.
            if (current == root) break;
        }
        return false;
    }
}
