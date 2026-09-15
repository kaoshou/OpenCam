using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using Serilog;

namespace ScreenRecorder.Infrastructure.Recovery;

public class RecordingRecoveryService : IRecordingRecoveryService
{
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

            var mkvPath = _storageService.GetWorkingFilePath(session.WorkingDirectory);
            bool hasFile = File.Exists(mkvPath);
            long fileSize = 0;
            if (hasFile)
            {
                try { fileSize = new FileInfo(mkvPath).Length; } catch { }
            }

            // 只要檔案存在且非 0 byte，即具備救援價值
            if (hasFile && fileSize > 0)
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

    public async Task<(bool Success, string? ErrorMessage, string? FinalMp4Path)> RecoverSessionAsync(
        string sessionDirectory, 
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadSessionAsync(sessionDirectory, cancellationToken);
        if (session == null)
        {
            return (false, "找不到指定的 Session 資料", null);
        }

        var segments = session.SegmentFilePaths != null && session.SegmentFilePaths.Count > 0
            ? session.SegmentFilePaths.Where(File.Exists).ToList()
            : Directory.GetFiles(sessionDirectory, "segment_*.mkv").OrderBy(f => f).ToList();

        if (segments.Count == 0)
        {
            var fallbackMkv = _storageService.GetWorkingFilePath(sessionDirectory);
            if (File.Exists(fallbackMkv))
            {
                segments.Add(fallbackMkv);
            }
        }

        if (segments.Count == 0)
        {
            return (false, "在工作階段目錄中找不到任何有效的 MKV 檔案", null);
        }

        // 決定救援輸出路徑
        var rootDir = Path.GetDirectoryName(session.WorkingDirectory);
        if (rootDir != null && rootDir.EndsWith("Sessions", StringComparison.OrdinalIgnoreCase))
        {
            rootDir = Path.GetDirectoryName(rootDir);
        }
        rootDir ??= _storageService.GetDefaultRecordingsPath();

        var outputMp4 = Path.Combine(rootDir, $"Recovered_{session.SessionId}.mp4");
        int counter = 1;
        while (File.Exists(outputMp4))
        {
            outputMp4 = Path.Combine(rootDir, $"Recovered_{session.SessionId}_{counter}.mp4");
            counter++;
        }

        try
        {
            bool success;
            if (segments.Count > 1)
            {
                Log.Information("開始執行多段 Crash Recovery 拼接救援: {Count} 個分段 -> {Mp4}", segments.Count, outputMp4);
                success = await _remuxer.ConcatAndRemuxToMp4Async(segments, outputMp4, null, cancellationToken);
            }
            else
            {
                Log.Information("開始執行單段 Crash Recovery 救援轉碼: {Mkv} -> {Mp4}", segments[0], outputMp4);
                success = await _remuxer.RemuxToMp4Async(segments[0], outputMp4, null, cancellationToken);
            }

            if (!success || !File.Exists(outputMp4))
            {
                return (false, "無損轉碼失敗，請確認 MKV 檔案未被其他程序鎖定", null);
            }

            var probeResult = await _probeService.ProbeAsync(outputMp4, cancellationToken);
            if (!probeResult.IsValid || probeResult.VideoStreamCount < 1)
            {
                return (false, "修復後的檔案格式無效", null);
            }

            // 記錄 recovery.json
            var recoveryMetadata = new
            {
                RecoveredAt = DateTimeOffset.Now,
                OriginalSessionId = session.SessionId,
                WorkingFilePaths = segments,
                OutputMp4Path = outputMp4,
                Duration = probeResult.Duration,
                FileSize = probeResult.FileSizeBytes
            };

            var recoveryJsonPath = Path.Combine(sessionDirectory, "recovery.json");
            await File.WriteAllTextAsync(recoveryJsonPath, JsonSerializer.Serialize(recoveryMetadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            // 更新 session.json
            session.State = RecordingState.Completed;
            session.FinalFilePath = outputMp4;
            session.FileSizeBytes = probeResult.FileSizeBytes;
            session.StopReason = "Crash Recovery 成功救回";
            await _sessionStore.SaveSessionAsync(session, cancellationToken);

            Log.Information("Crash Recovery 救援成功: {Mp4}, 時長 {Dur}", outputMp4, probeResult.Duration);
            return (true, null, outputMp4);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Crash Recovery 執行失敗");
            return (false, ex.Message, null);
        }
    }
}
