// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
using Serilog;

namespace ScreenRecorder.Infrastructure.Session;

public class JsonRecordingSessionStore : IRecordingSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveSessionAsync(RecordingSession session, CancellationToken cancellationToken = default)
    {
        var dir = session.WorkingDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sessionFilePath = Path.Combine(dir, "session.json");
        var tempFilePath = Path.Combine(dir, "session.json.tmp");
        var backupFilePath = Path.Combine(dir, "session.json.bak");

        var json = JsonSerializer.Serialize(session, JsonOptions);

        // 先寫入臨時檔案並 Flush 至磁碟
        await File.WriteAllTextAsync(tempFilePath, json, cancellationToken);

        // 如果現有 session.json 存在，先備份
        if (File.Exists(sessionFilePath))
        {
            File.Copy(sessionFilePath, backupFilePath, overwrite: true);
        }

        // 移動/替換為正式 session.json
        File.Move(tempFilePath, sessionFilePath, overwrite: true);
    }

    public async Task<RecordingSession?> LoadSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default)
    {
        var sessionFilePath = Path.Combine(sessionDirectory, "session.json");
        var backupFilePath = Path.Combine(sessionDirectory, "session.json.bak");

        if (!File.Exists(sessionFilePath))
        {
            if (File.Exists(backupFilePath))
            {
                sessionFilePath = backupFilePath;
            }
            else
            {
                return TryReconstructInterruptedSession(sessionDirectory);
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(sessionFilePath, cancellationToken);
            return JsonSerializer.Deserialize<RecordingSession>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception primaryException)
        {
            Log.Warning(
                primaryException,
                "錄影工作階段中繼資料無法解析: {SessionFile}",
                sessionFilePath);
            if (File.Exists(backupFilePath) &&
                !string.Equals(sessionFilePath, backupFilePath, StringComparison.Ordinal))
            {
                try
                {
                    var backupJson = await File.ReadAllTextAsync(backupFilePath, cancellationToken);
                    var backupSession = JsonSerializer.Deserialize<RecordingSession>(backupJson, JsonOptions);
                    if (backupSession != null)
                    {
                        return backupSession;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception backupException)
                {
                    Log.Warning(
                        backupException,
                        "錄影工作階段備份中繼資料亦無法解析: {BackupFile}",
                        backupFilePath);
                }
            }

            return TryReconstructInterruptedSession(sessionDirectory);
        }
    }

    public async Task<IReadOnlyList<RecordingSession>> FindAllSessionsAsync(string rootRecordingsPath, CancellationToken cancellationToken = default)
    {
        var sessionsDir = Path.Combine(rootRecordingsPath, "Sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return Array.Empty<RecordingSession>();
        }

        var results = new List<RecordingSession>();
        foreach (var dir in Directory.EnumerateDirectories(sessionsDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordingSession? session;
            try
            {
                session = await LoadSessionAsync(dir, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "讀取錄影工作階段中繼資料失敗，嘗試由 MKV 重建: {Directory}", dir);
                session = null;
            }

            session ??= TryReconstructInterruptedSession(dir);
            if (session != null)
            {
                results.Add(session);
            }
        }

        return results.OrderByDescending(s => s.StartTime).ToList();
    }

    private static RecordingSession? TryReconstructInterruptedSession(string directory)
    {
        try
        {
            var segments = Directory
                .EnumerateFiles(directory, "segment_*.mkv")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var legacyFile = Path.Combine(directory, "recording.mkv");
            if (File.Exists(legacyFile))
            {
                segments.Add(legacyFile);
            }

            segments = segments
                .Where(path => new FileInfo(path).Length > 0)
                .Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .ToList();
            if (segments.Count == 0)
            {
                return null;
            }

            var newestWriteTime = segments
                .Select(File.GetLastWriteTimeUtc)
                .Max();
            return new RecordingSession
            {
                SessionId = Path.GetFileName(directory),
                StartTime = new DateTimeOffset(newestWriteTime, TimeSpan.Zero),
                State = RecordingState.Interrupted,
                WorkingDirectory = directory,
                WorkingFilePath = segments[^1],
                SegmentFilePaths = segments,
                StopReason = "工作階段中繼資料遺失或損壞，已由 MKV 分段重建"
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "無法由 MKV 重建錄影工作階段: {Directory}", directory);
            return null;
        }
    }

    public Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(sessionDirectory))
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
        return Task.CompletedTask;
    }
}
