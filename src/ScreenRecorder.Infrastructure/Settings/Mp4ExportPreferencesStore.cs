// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using Serilog;

namespace ScreenRecorder.Infrastructure.Settings;

public sealed class Mp4ExportPreferencesStore
{
    private const int MaximumFileBytes = 4096;
    private readonly string _path;

    public Mp4ExportPreferencesStore(string? customPath = null) => _path = customPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder", "mp4-export.json");

    public async Task<Mp4ExportOptions> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            var buffer = new byte[MaximumFileBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumFileBytes) throw new InvalidDataException("MP4 export preferences are too large.");
            var options = JsonSerializer.Deserialize<Mp4ExportOptions>(buffer.AsSpan(0, length))
                ?? throw new InvalidDataException("MP4 export preferences are empty.");
            options.Validate();
            return options;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Log.Warning(ex, "Unable to read MP4 export preferences; using original settings");
            return new();
        }
    }

    public async Task SaveAsync(Mp4ExportOptions options, CancellationToken cancellationToken = default)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, ".mp4-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Unable to save MP4 export preferences");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Log.Warning(ex, "Unable to remove temporary MP4 export preferences"); }
            }
        }
    }
}
