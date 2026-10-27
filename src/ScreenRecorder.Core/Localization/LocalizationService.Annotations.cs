// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;

public partial class LocalizationService
{
    static LocalizationService()
    {
        foreach(var row in new (string Key,string Zh,string En,string Ja)[]{
            ("AnnotationsTitle","螢幕註記","Screen annotations","画面への書き込み"),
            ("AnnotationsEnabled","啟用螢幕註記工具","Enable screen annotation tools","画面への書き込みツールを有効にする"),
            ("AnnotationsHelp","錄影時可使用畫筆與圖形標示重點。預設保持正常操作，按螢幕註記後才開啟工具；關閉不影響一般錄影。","Use pens and shapes while recording. Normal mouse operation remains until you open the tools. Disabling this does not affect recording.","録画中にペンや図形で要点を示せます。ツールを開くまでは通常のマウス操作のままです。無効にしても通常の録画には影響しません。"),
            ("AnnotationsCaptured","註記會直接錄入影片，事後無法移除；固定在螢幕位置，不隨教材捲動。","Annotations are recorded into the video and cannot be removed later. They stay at screen positions, not with scrolling content.","書き込みは動画に記録され、後から削除できません。画面上の位置に固定され、教材のスクロールには追従しません。"),
            ("AnnotationsDrawing","註記中","Drawing","書き込み中"),
            ("AnnotationsUnavailable","螢幕註記無法使用，影音錄製仍會繼續。","Annotations unavailable; video and audio recording continue.","書き込みを使用できません。映像と音声の録画は継続します。"),
            ("AnnotationsOperating","操作中","Operating","操作中"),
            ("AnnotationsOperate","操作","Operate","操作"),
            ("AnnotationsPen","畫筆","Pen","ペン"),("AnnotationsLine","直線","Line","直線"),
            ("AnnotationsArrow","箭頭","Arrow","矢印"),("AnnotationsRectangle","方框","Rectangle","四角"),
            ("AnnotationsEllipse","圓圈","Ellipse","丸"),("AnnotationsRemove","移除一筆","Remove a mark","書き込みを1つ削除"),
            ("AnnotationsUndo","復原","Undo","元に戻す"),("AnnotationsRedo","重做","Redo","やり直す"),
            ("AnnotationsClear","清除全部","Clear all","すべて消去"),("AnnotationsWidth","線條粗細","Line width","線の太さ"),
            ("AnnotationsClose","關閉註記","Close annotations","書き込みを閉じる"),
            ("AnnotationsMinimize","最小化 OpenCam 主視窗","Minimize the OpenCam window","OpenCam のメイン画面を最小化"),
            ("AnnotationsShortcut","開啟註記：Ctrl + Shift + F8","Open annotations: Ctrl + Shift + F8","書き込みを開く：Ctrl + Shift + F8"),
            ("AnnotationsHide","隱藏工具列；按 Ctrl + Shift + F8 重新顯示","Hide toolbar; press Ctrl + Shift + F8 to show it again","ツールバーを隠す。Ctrl + Shift + F8 で再表示"),
            ("AnnotationsCollapse","展開／收合工具","Expand / collapse tools","ツールを展開／折りたたむ"),
            ("AnnotationsDrag","拖曳移動工具列","Drag to move toolbar","ドラッグしてツールバーを移動"),
            ("AnnotationsEsc","按住左鍵才繪圖 · Esc 返回操作 · 註記與工具列可能錄入影片","Hold left button to draw · Esc to operate · Marks and toolbar may appear in the recording","左ボタンを押しながら描画 · Esc で操作に戻る · 書き込みとツールバーが録画に含まれる場合があります"),
            ("AnnotationsColorRed","紅色","Red","赤"),("AnnotationsColorOrange","橙色","Orange","オレンジ"),
            ("AnnotationsColorYellow","黃色","Yellow","黄"),("AnnotationsColorGreen","綠色","Green","緑"),
            ("AnnotationsColorBlue","藍色","Blue","青"),("AnnotationsColorBlack","黑色","Black","黒"),
            ("AnnotationsColorWhite","白色","White","白")})
        {ZhTwDictionary[row.Key]=row.Zh;EnUsDictionary[row.Key]=row.En;JaJpDictionary[row.Key]=row.Ja;}
    }
}
