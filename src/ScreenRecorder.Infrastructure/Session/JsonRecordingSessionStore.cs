using System.Text.Json;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

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
                return null;
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(sessionFilePath, cancellationToken);
            return JsonSerializer.Deserialize<RecordingSession>(json, JsonOptions);
        }
        catch when (File.Exists(backupFilePath))
        {
            // 嘗試從備份還原
            var backupJson = await File.ReadAllTextAsync(backupFilePath, cancellationToken);
            return JsonSerializer.Deserialize<RecordingSession>(backupJson, JsonOptions);
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
            var session = await LoadSessionAsync(dir, cancellationToken);
            if (session != null)
            {
                results.Add(session);
            }
        }

        return results.OrderByDescending(s => s.StartTime).ToList();
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
