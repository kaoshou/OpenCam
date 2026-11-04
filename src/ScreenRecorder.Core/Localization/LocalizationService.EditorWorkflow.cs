// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;
public partial class LocalizationService
{
    private static void AddEditorWorkflowStrings()
    {
        foreach(var row in new (string Key,string Zh,string En,string Ja)[]{
            ("EditorPreviousFrame","前一格","Previous frame","前のフレーム"),
            ("EditorNextFrame","後一格","Next frame","次のフレーム"),
            ("EditorPreviewRange","播放選取範圍","Play selection","選択範囲を再生"),
            ("EditorRepeatRange","循環試聽","Repeat selection","選択範囲を繰り返す"),
            ("EditorSnapping","邊界吸附","Snap to edges","境界にスナップ"),
            ("EditorSnapHint","吸附至片段邊界或書籤；按住 Alt 暫時略過。","Snap to clip edges or bookmarks. Hold Alt to bypass.","クリップ境界やブックマークにスナップ。Altで一時解除。"),
            ("EditorRangeHint","以 I／O 標記選取範圍起點與終點。","Mark a selection start/end with I / O.","I / Oで選択範囲の開始・終了を指定。"),
            ("EditorKeyboardHelp","鍵盤操作","Keyboard controls","キーボード操作"),
            ("EditorSecondSeek","前後一秒","Seek one second","1秒移動"),
            ("EditorKeyboardScope","僅在剪輯器作用；輸入文字、調整控制項時保留原本操作。","Editor only. Text fields and focused controls keep their own keys.","編集画面のみ。入力欄や操作中のコントロールでは通常のキー操作を優先。"),
            ("EditorMeterTitle","預覽混音","Preview mix","プレビュー音声"),
            ("EditorMeterIdle","預覽音量表：待命","Preview meter: idle","プレビュー音量：待機"),
            ("EditorMeterNoData","預覽音量表：等待取樣","Preview meter: awaiting samples","プレビュー音量：サンプル待ち"),
            ("EditorMeterMuted","預覽已靜音（顯示混音音量）","Preview muted (showing mix level)","プレビュー消音中（ミックス音量を表示）"),
            ("EditorMeterOverload","可能削波，請降低片段音量","Possible clipping: reduce clip volume","クリッピングの可能性：クリップ音量を下げてください"),
            ("EditorAudioEffects","片段音訊","Clip audio","クリップ音声"),
            ("EditorNoExport","尚無輸出紀錄；儲存專案不等於輸出影片。","No export recorded. Saving a project does not export a video.","出力記録なし。プロジェクト保存と動画出力は別です。"),
            ("EditorCurrentExport","最近輸出：目前修訂","Latest export: current revision","直近の出力：現在のリビジョン"),
            ("EditorOlderExport","最近輸出：較早或其他修訂，未包含目前全部修改","Latest export: earlier or different revision; current changes may not be included","直近の出力：以前または別のリビジョン。現在の変更が含まれない場合があります"),
            ("EditorRangePlaying","正在預覽選取範圍","Playing selection","選択範囲を再生中"),
        }){ZhTwDictionary[row.Key]=row.Zh;EnUsDictionary[row.Key]=row.En;JaJpDictionary[row.Key]=row.Ja;}
    }
}
