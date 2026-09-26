// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using Serilog;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Recovery;

public class RecordingRecoveryService : IRecordingRecoveryService
{
    public static readonly TimeSpan ActiveHeartbeatTimeout = TimeSpan.FromSeconds(15);

    private readonly IRecordingSessionStore _sessionStore;
    private readonly IStreamCopyRemuxer _remuxer;
    private readonly IMediaProbeService _probeService;
    private readonly IStorageService _storageService;

    public RecordingRecoveryService(
        IRecordingSessionStore sessionStore,
        IStreamCopyRemuxer remuxer,
        IMediaProbeService probeService,
        IStorageService storageService)
    {
        _sessionStore = sessionStore;
        _remuxer = remuxer;
        _probeService = probeService;
        _storageService = storageService;
    }

    public async Task<IReadOnlyList<RecoverableSessionInfo>> ScanForRecoverableSessionsAsync(
        string rootRecordingsPath, 
        CancellationToken cancellationToken = default)
    {
        var sessions = await _sessionStore.FindAllSessionsAsync(rootRecordingsPath, cancellationToken);
        var recoverableList = new List<RecoverableSessionInfo>();

        foreach (var session in sessions)
        {
            // 已正常 Completed 且非異常的不需要救援
            if (session.State == RecordingState.Completed)
            {
                continue;
            }

            if (IsSessionActive(session, DateTimeOffset.UtcNow))
            {
                continue;
            }

            List<FileInfo> segments;
            try { segments = FindRecoverableSegments(session); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Log.Warning(ex, "略過不安全的救援工作階段: {Directory}", session.WorkingDirectory);
                continue;
            }
            var fileSize = segments.Sum(segment => segment.Length);

            // 只要至少一個分段存在且非 0 byte，即具備救援價值
            if (segments.Count > 0 && fileSize > 0)
            {
                var desc = session.State switch
                {
                    RecordingState.Recording => "錄影中途非正常中斷 (可能為斷電或強制關閉)",
                    RecordingState.Interrupted => $"錄影異常中斷: {session.StopReason ?? session.ErrorMessage ?? "未知中斷"}",
                    RecordingState.Failed => $"錄影失敗殘留檔: {session.ErrorMessage ?? "未知錯誤"}",
                    RecordingState.Finalizing => "轉碼階段非正常中止",
                    _ => "未完成之工作階段"
                };

                recoverableList.Add(new RecoverableSessionInfo(session, fileSize, true, desc));
            }
        }

        return recoverableList;
    }

    public async Task<(bool Success, bool IsPartial, string? ErrorMessage, string? FinalMp4Path)> RecoverSessionAsync(
        string sessionDirectory, 
        CancellationToken cancellationToken = default)
    {
        BoundDirectory bound;
        BoundDirectory destination;
        try
        {
            bound = BoundDirectory.Open(sessionDirectory);
            try
            {
                var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory)))!;
                var root = string.Equals(Path.GetFileName(parent), "Sessions", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(parent)! : parent;
                destination = BoundDirectory.Open(root);
            }
            catch { bound.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return (false, false, $"無法安全開啟救援目錄: {ex.Message}", null); }
        using var boundScope = bound;
        using var destinationScope = destination;
        if (_sessionStore is not JsonRecordingSessionStore boundStore)
            return (false, false, "工作階段儲存服務不支援安全救援", null);
        RecordingSession? session;
        try
        {
            sessionDirectory = SessionPathPolicy.DirectoryPath(sessionDirectory);
            foreach (var leaf in new[] { ".recovery.lock", "recovery.json", "session.json", "session.json.bak", "session.json.tmp" })
                SessionPathPolicy.RejectLink(Path.Combine(sessionDirectory, leaf));
            session = SessionPathPolicy.Bind(await boundStore.LoadBoundAsync(sessionDirectory, bound, cancellationToken), sessionDirectory);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return (false, false, $"不安全或無法讀取的救援路徑: {ex.Message}", null);
        }
        if (session == null)
        {
            return (false, false, "找不到指定的 Session 資料", null);
        }

        if (IsSessionActive(session, DateTimeOffset.UtcNow))
        {
            return (false, false, "錄影工作階段仍在執行，暫不允許救援", null);
        }

        List<string> expectedSegments;
        try { expectedSegments = FindRecoverySegmentPaths(session); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return (false, false, $"不安全的救援分段: {ex.Message}", null);
        }
        var segments = FindRecoverableSegments(expectedSegments)
            .Select(segment => segment.FullName)
            .ToList();

        if (segments.Count == 0)
        {
            return (false, false, "在工作階段目錄中找不到任何有效的 MKV 檔案", null);
        }

        FileStream recoveryClaim;
        try
        {
            recoveryClaim = bound.Claim();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (false, false, "此工作階段正在由另一個 OpenCam 實例救援，或救援鎖檔不安全／無法存取", null);
        }
        using var recoveryClaimScope = recoveryClaim;

        // 決定救援輸出路徑
        var rootDir = Path.GetDirectoryName(session.WorkingDirectory);
        if (rootDir != null && rootDir.EndsWith("Sessions", StringComparison.OrdinalIgnoreCase))
        {
            rootDir = Path.GetDirectoryName(rootDir);
        }
        rootDir ??= _storageService.GetDefaultRecordingsPath();

        // Never let FFmpeg's overwrite flag operate in the user-selected output
        // directory. Publish a verified result with a no-overwrite move instead.
        var staging = Directory.CreateTempSubdirectory("OpenCam-recovery-");
        var outputMp4 = Path.Combine(staging.FullName, "recovered.mp4");
        var publishedMp4 = Path.Combine(rootDir, $"Recovered_{session.SessionId}.mp4");
        int counter = 1;
        while (Path.Exists(publishedMp4) || new FileInfo(publishedMp4).LinkTarget != null)
        {
            publishedMp4 = Path.Combine(rootDir, $"Recovered_{session.SessionId}_{counter}.mp4");
            counter++;
        }

        string? preservedOutput = null;
        try
        {
            // Probe and FFmpeg see only private snapshots, never names that may
            // be replaced on shared storage between validation and remux.
            var originalPaths = new Dictionary<string, string>();
            var snapshots = new List<string>();
            foreach (var source in segments)
            {
                var snapshot = Path.Combine(staging.FullName, $"segment_{snapshots.Count:D6}.mkv");
                using var input = bound.Read(Path.GetFileName(source));
                await using var output = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
                snapshots.Add(snapshot);
                originalPaths.Add(snapshot, source);
            }
            var usableSegments = await FindUsableSegmentsAsync(
                snapshots,
                cancellationToken);
            if (usableSegments.Count == 0)
            {
                return (false, false, "所有 MKV 分段皆已損壞或不含有效視訊，無法救援", null);
            }

            var recoveredSegments = usableSegments;
            var success = await RemuxSegmentsAsync(
                recoveredSegments,
                outputMp4,
                cancellationToken);

            // 若完整拼接仍失敗，逐步捨棄最末段，至少保住前面連續且可讀的內容。
            for (var count = usableSegments.Count - 1; !success && count >= 1; count--)
            {
                recoveredSegments = usableSegments.Take(count).ToList();
                Log.Warning(
                    "完整救援拼接失敗，改以最前方 {Count} 個有效分段重試",
                    count);
                success = await RemuxSegmentsAsync(
                    recoveredSegments,
                    outputMp4,
                    cancellationToken);
            }

            if (!success || !File.Exists(outputMp4))
            {
                return (false, false, "無損轉碼失敗，請確認 MKV 檔案未被其他程序鎖定", null);
            }

            var probeResult = await _probeService.ProbeAsync(outputMp4, cancellationToken);
            if (!probeResult.IsValid || probeResult.VideoStreamCount < 1)
            {
                return (false, false, "修復後的檔案格式無效", null);
            }

            var isPartial = recoveredSegments.Count < expectedSegments.Count;
            outputMp4 = await destination.PublishAsync(outputMp4, Path.GetFileName(publishedMp4), cancellationToken);
            preservedOutput = outputMp4;

            // 記錄 recovery.json
            var recoveryMetadata = new
            {
                RecoveredAt = DateTimeOffset.Now,
                OriginalSessionId = session.SessionId,
                WorkingFilePaths = expectedSegments,
                RecoveredSegmentPaths = recoveredSegments.Select(path => originalPaths[path]).ToList(),
                IsPartial = isPartial,
                OutputMp4Path = outputMp4,
                Duration = probeResult.Duration,
                FileSize = probeResult.FileSizeBytes
            };

            var recoveryJsonPath = Path.Combine(sessionDirectory, "recovery.json");
            await bound.WriteTextAsync("recovery.json", JsonSerializer.Serialize(recoveryMetadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            // 更新 session.json
            session.State = RecordingState.Completed;
            session.FinalFilePath = outputMp4;
            session.FileSizeBytes = probeResult.FileSizeBytes;
            session.StopReason = isPartial
                ? $"Crash Recovery 部分救回 ({recoveredSegments.Count}/{expectedSegments.Count} 個分段)"
                : "Crash Recovery 完整救回";
            await boundStore.SaveBoundAsync(session, bound, cancellationToken);

            Log.Information("Crash Recovery 救援成功: {Mp4}, 時長 {Dur}", outputMp4, probeResult.Duration);
            var warning = isPartial
                ? $"僅救回 {recoveredSegments.Count}/{expectedSegments.Count} 個分段；損壞段落未寫入 MP4，原始 MKV 已保留"
                : null;
            return (true, isPartial, warning, outputMp4);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Crash Recovery 執行失敗");
            return (false, false, preservedOutput == null ? ex.Message : $"MP4 已保留於 {preservedOutput}，但救援紀錄更新失敗: {ex.Message}", preservedOutput);
        }
        finally
        {
            try { staging.Delete(recursive: true); }
            catch (IOException ex) { Log.Warning(ex, "無法清除救援暫存目錄"); }
        }
    }

    private async Task<List<string>> FindUsableSegmentsAsync(
        IReadOnlyList<string> segments,
        CancellationToken cancellationToken)
    {
        var usable = new List<string>();
        foreach (var segment in segments)
        {
            try
            {
                var result = await _probeService.ProbeAsync(segment, cancellationToken);
                if (result.IsValid && result.VideoStreamCount > 0)
                {
                    usable.Add(segment);
                }
                else
                {
                    Log.Warning("略過無法解析的救援分段: {Segment}", segment);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "檢查救援分段時發生錯誤，已略過: {Segment}", segment);
            }
        }

        return usable;
    }

    private async Task<bool> RemuxSegmentsAsync(
        IReadOnlyList<string> segments,
        string outputMp4,
        CancellationToken cancellationToken)
    {
        if (segments.Count > 1)
        {
            Log.Information(
                "開始執行多段 Crash Recovery 拼接救援: {Count} 個分段 -> {Mp4}",
                segments.Count,
                outputMp4);
            return await _remuxer.ConcatAndRemuxToMp4Async(
                segments,
                outputMp4,
                null,
                cancellationToken);
        }

        Log.Information(
            "開始執行單段 Crash Recovery 救援轉碼: {Mkv} -> {Mp4}",
            segments[0],
            outputMp4);
        return await _remuxer.RemuxToMp4Async(
            segments[0],
            outputMp4,
            null,
            cancellationToken);
    }

    private static bool IsSessionActive(
        RecordingSession session,
        DateTimeOffset now)
    {
        if (session.LastHeartbeatTime == default)
        {
            return false;
        }

        var isPotentiallyActive = session.State is
            RecordingState.Preparing or
            RecordingState.Recording or
            RecordingState.Pausing or
            RecordingState.Paused or
            RecordingState.Stopping or
            RecordingState.Finalizing;
        if (!isPotentiallyActive)
        {
            return false;
        }

        var heartbeatAge = now - session.LastHeartbeatTime;
        return heartbeatAge >= -ActiveHeartbeatTimeout &&
               heartbeatAge <= ActiveHeartbeatTimeout;
    }

    private List<FileInfo> FindRecoverableSegments(RecordingSession session) =>
        FindRecoverableSegments(FindRecoverySegmentPaths(session));

    private static List<FileInfo> FindRecoverableSegments(
        IReadOnlyList<string> paths)
    {
        var result = new List<FileInfo>();
        foreach (var path in paths)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > 0)
                {
                    result.Add(file);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "掃描救援分段失敗: {Path}", path);
            }
        }

        return result;
    }

    private List<string> FindRecoverySegmentPaths(RecordingSession session)
    {
        var paths = new List<string>();
        if (session.SegmentFilePaths != null)
        {
            paths.AddRange(session.SegmentFilePaths);
        }

        if (!string.IsNullOrWhiteSpace(session.WorkingFilePath))
        {
            paths.Add(session.WorkingFilePath);
        }

        if (Directory.Exists(session.WorkingDirectory))
        {
            try
            {
                paths.AddRange(Directory
                    .EnumerateFiles(session.WorkingDirectory, "segment_*.mkv")
                    .OrderBy(path => path, StringComparer.Ordinal));
            }
            catch (Exception ex)
            {
                Log.Warning(
                    ex,
                    "無法列舉救援工作階段目錄: {WorkingDirectory}",
                    session.WorkingDirectory);
            }
        }

        // 相容 v0.1.0 與更早版本的單一工作檔名稱；僅在實際存在或沒有
        // 其他候選路徑時納入，避免把新格式不存在的 recording.mkv 誤算成遺失分段。
        var legacyPath = _storageService.GetWorkingFilePath(session.WorkingDirectory);
        if (File.Exists(legacyPath) || paths.Count == 0)
        {
            paths.Add(legacyPath);
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => SessionPathPolicy.SegmentPath(session.WorkingDirectory, path))
            .Distinct(comparer)
            .ToList();
    }
}
