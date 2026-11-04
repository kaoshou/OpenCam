// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;

public partial class LocalizationService
{
    private static void AddSupportStrings()
    {
        foreach (var row in new (string Key, string Zh, string En, string Ja)[] {
            ("SupportTitle", "支援與診斷", "Support and diagnostics", "サポートと診断"),
            ("SupportOpenLogs", "開啟日誌資料夾", "Open log folder", "ログフォルダーを開く"),
            ("SupportCopyEnvironment", "複製環境資訊", "Copy environment info", "環境情報をコピー"),
            ("SupportExport", "匯出診斷資料…", "Export diagnostics…", "診断情報をエクスポート…"),
            ("SupportCheckUpdates", "檢查更新", "Check for updates", "更新を確認"),
            ("SupportUpdateHint", "在瀏覽器開啟官方版本頁面，不會自動下載或安裝。", "Open the official releases page in your browser. Nothing is downloaded or installed automatically.", "公式リリースページをブラウザーで開きます。自動ダウンロードやインストールは行いません。"),
            ("SupportPrivacy", "診斷資料只包含環境摘要與最近四天的應用程式日誌（每檔最多 2 MiB，過大或無法讀取時略過），不包含錄影、音訊、字幕、專案或設定檔。日誌可能含檔案路徑與裝置名稱，分享前請先檢查；不會自動上傳。", "Diagnostics include an environment summary and app logs from the last four days (up to 2 MiB each; oversized or unreadable logs are omitted), not recordings, audio, subtitles, projects or settings files. Logs may contain file paths and device names. Review before sharing; nothing is uploaded automatically.", "診断情報は環境の概要と直近4日間のアプリログのみです（各2 MiBまで。大きすぎるものや読み取れないものは除外）。録画・音声・字幕・プロジェクト・設定ファイルは含みません。ログにはパスや機器名が含まれる場合があります。共有前に確認してください。自動送信はしません。"),
            ("SupportCopied", "已複製環境資訊，不含日誌內容。", "Environment info copied; log contents are not included.", "環境情報をコピーしました。ログの内容は含みません。"),
            ("SupportOpened", "已開啟日誌資料夾。", "Log folder opened.", "ログフォルダーを開きました。"),
            ("SupportUpdateOpened", "已開啟官方版本頁面，請與目前版本比較。", "Official releases page opened. Compare it with your current version.", "公式リリースページを開きました。現在のバージョンと比較してください。"),
            ("SupportBusy", "正在整理診斷資料…", "Preparing diagnostics…", "診断情報を準備しています…"),
            ("SupportSaved", "診斷資料已儲存，尚未分享；請先檢查壓縮檔內容。", "Diagnostics saved, not shared. Review the archive before sharing.", "診断情報を保存しました。まだ共有されていません。共有前に内容を確認してください。"),
            ("SupportFailed", "無法完成操作，請確認儲存位置、檔案權限，或改用新的檔名。", "Unable to complete the action. Check the location and permissions, or choose a new filename.", "操作を完了できません。保存先と権限を確認するか、新しいファイル名を指定してください。"),
            ("SupportUnavailable", "目前無法使用剪貼簿或系統開啟功能。", "The clipboard or system opener is unavailable.", "クリップボードまたはシステムの起動機能を使用できません。"),
        }) { ZhTwDictionary[row.Key] = row.Zh; EnUsDictionary[row.Key] = row.En; JaJpDictionary[row.Key] = row.Ja; }
    }
}
