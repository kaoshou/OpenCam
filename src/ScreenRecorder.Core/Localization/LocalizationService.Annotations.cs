// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;

public partial class LocalizationService
{
    static LocalizationService()
    {
        foreach(var row in new (string Key,string Zh,string En,string Ja)[]{
            ("LinuxTestNotice","Linux 開發測試版：目前使用 CPU 編碼；教鞭、螢幕註記與全域快捷鍵尚未開放。Wayland 請在系統分享視窗選擇相同尺寸的目標螢幕。請先短錄並檢查影音。","Linux development test: CPU encoding; teaching pointers, annotations and global shortcuts are not yet enabled. On Wayland, choose the matching-size display in the system sharing dialog. Verify a short recording first.","Linux 開発テスト版：CPU エンコードを使用します。教鞭、画面への書き込み、グローバルショートカットは未対応です。Wayland ではシステムの共有画面で同じ解像度の画面を選び、まず短い録画を確認してください。"),
            ("AnnotationsTitle","螢幕註記","Screen annotations","画面への書き込み"),
            ("AnnotationsHotkeyTogglePen","開啟／關閉畫筆","Toggle drawing","描画の切り替え"),
            ("AnnotationsHotkeyToolbar","開啟／隱藏工具列","Show / hide toolbar","ツールバーの表示切り替え"),
            ("AnnotationsHotkeyFinish","結束繪圖並清除註記","Finish drawing and clear marks","描画を終了して消去"),
            ("AnnotationsShortcutsHeader","螢幕註記快捷鍵","Screen annotation shortcuts","画面への書き込みショートカット"),
            ("HotkeyNoModifier","無","None","なし"),
            ("AnnotationsShortcutsHelp","註記快捷鍵在錄影時使用。結束繪圖並清除註記的按鍵，僅在繪圖畫面或工具列有焦點時生效。","Annotation shortcuts work during recording. The finish-and-clear shortcut works when the drawing surface or toolbar has focus.","書き込みショートカットは録画中に使用できます。描画の終了と消去は、描画画面またはツールバーにフォーカスがあるときに動作します。"),
            ("HotkeyF12Reserved","Windows 的 F12 保留給偵錯器，請選擇其他全域快捷鍵。","Windows reserves F12 for the debugger; choose another global shortcut.","Windows では F12 はデバッガー用に予約されています。別のグローバルショートカットを選択してください。"),
            ("AnnotationsEnabled","啟用螢幕註記工具","Enable screen annotation tools","画面への書き込みツールを有効にする"),
            ("AnnotationsHelp","錄影時可使用畫筆與圖形標示重點。預設保持正常操作，按螢幕註記後才開啟工具；關閉不影響一般錄影。","Use pens and shapes while recording. Normal mouse operation remains until you open the tools. Disabling this does not affect recording.","録画中にペンや図形で要点を示せます。ツールを開くまでは通常のマウス操作のままです。無効にしても通常の録画には影響しません。"),
            ("AnnotationsCaptured","註記會直接錄入影片，事後無法移除；固定在螢幕位置，不隨教材捲動。","Annotations are recorded into the video and cannot be removed later. They stay at screen positions, not with scrolling content.","書き込みは動画に記録され、後から削除できません。画面上の位置に固定され、教材のスクロールには追従しません。"),
            ("AnnotationsDrawing","註記中","Drawing","書き込み中"),
            ("AnnotationsUnavailable","螢幕註記無法使用，影音錄製仍會繼續。","Annotations unavailable; video and audio recording continue.","書き込みを使用できません。映像と音声の録画は継続します。"),
            ("AnnotationsOperating","操作中","Operating","操作中"),
            ("AnnotationsOperate","操作","Operate","操作"),
            ("AnnotationsPen","畫筆","Pen","ペン"),("AnnotationsLine","直線","Line","直線"),
            ("AnnotationsArrow","箭頭","Arrow","矢印"),("AnnotationsRectangle","方框","Rectangle","四角"),
            ("AnnotationsEllipse","圓圈","Ellipse","丸"),("AnnotationsRemove","橡皮擦：按住左鍵拖曳移除筆跡","Eraser: hold and drag to remove marks","消しゴム：左ボタンを押したままドラッグして書き込みを消去"),
            ("AnnotationsUndo","復原","Undo","元に戻す"),("AnnotationsRedo","重做","Redo","やり直す"),
            ("AnnotationsClear","清除全部","Clear all","すべて消去"),("AnnotationsWidth","線條粗細","Line width","線の太さ"),
            ("AnnotationsClose","關閉註記","Close annotations","書き込みを閉じる"),
            ("AnnotationsMinimize","最小化 OpenCam 主視窗","Minimize the OpenCam window","OpenCam のメイン画面を最小化"),
            ("AnnotationsShortcut","快捷鍵可在設定中調整","Shortcuts can be changed in Settings","ショートカットは設定で変更できます"),
            ("AnnotationsHide","隱藏工具列","Hide toolbar","ツールバーを隠す"),
            ("AnnotationsCollapse","展開／收合工具","Expand / collapse tools","ツールを展開／折りたたむ"),
            ("AnnotationsCollapseHint","收合工具列","Collapse toolbar","ツールバーを折りたたむ"),
            ("AnnotationsExpandHint","展開工具列","Expand toolbar","ツールバーを展開"),
            ("AnnotationsDrag","拖曳移動工具列","Drag to move toolbar","ドラッグしてツールバーを移動"),
            ("AnnotationsEsc","按住左鍵繪圖 · 快捷鍵可在設定中調整","Hold left button to draw · Customize shortcuts in Settings","左ボタンを押して描画 · ショートカットは設定で変更できます"),
            ("AnnotationsColorRed","紅色","Red","赤"),("AnnotationsColorOrange","橙色","Orange","オレンジ"),
            ("AnnotationsColorYellow","黃色","Yellow","黄"),("AnnotationsColorGreen","綠色","Green","緑"),
            ("AnnotationsColorBlue","藍色","Blue","青"),("AnnotationsColorBlack","黑色","Black","黒"),
            ("AnnotationsColorWhite","白色","White","白")})
        {ZhTwDictionary[row.Key]=row.Zh;EnUsDictionary[row.Key]=row.En;JaJpDictionary[row.Key]=row.Ja;}
    }
}
