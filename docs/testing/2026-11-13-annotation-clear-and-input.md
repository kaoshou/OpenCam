# 螢幕註記：清除後殘影與操作模式

## 問題與查核

- 使用者回報：繪圖 → 操作 → 清除全部 → 橡皮擦，會瞬間閃現已清除筆跡；有筆跡時切回操作仍擋住底下程式。
- 已確認的程式路徑：`AnnotationCanvas` 先呼叫 `InvalidateVisual`，`AnnotationOverlayHost` 隨即在空白文件狀態隱藏視窗；下次選工具再呼叫 `Show`。此路徑會在空白影格完成前卸載顯示表面，有重新顯示舊合成緩衝區的風險。回歸測試已捕捉清除時的隱藏／重開行為；本次未在 Windows 桌面直接拍到使用者回報的瞬間殘影。
- Windows 原生政策原先只加入 `WS_EX_TRANSPARENT`、`WS_EX_NOACTIVATE` 與 `WS_EX_TOOLWINDOW`。Microsoft 對跨視窗滑鼠穿透的明確保證適用於 `WS_EX_LAYERED` 搭配 `WS_EX_TRANSPARENT`；原先設定缺少前者。這是可確認的政策缺漏，仍須實機確認是否完整解決使用者環境的阻擋情形。
- Avalonia 11.2.5 的 `WindowImpl.UpdateWindowProperties` 會重新計算 extended styles，因此只直接寫一次原生樣式不足以維持政策。

參考：[Microsoft Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)、[DirectComposition basic concepts](https://learn.microsoft.com/en-us/windows/win32/directcomp/basic-concepts)、[Avalonia 11.2.5 WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/11.2.5/src/Windows/Avalonia.Win32/WindowImpl.cs)。

## 變更

- 用過的註記表面清空後繼續保持顯示，讓空白影格正常繪出；不因清除或切換到操作模式立即 Hide／Show。初次開啟工具列仍不顯示全螢幕繪圖表面，暫停和結束仍正常隱藏／關閉。
- Windows 操作模式加入分層＋穿透、全透明度初始化及原生樣式更新；繪圖模式移除分層與穿透，讓空白區域仍可落筆。保留渲染器既有的其他樣式。
- 使用 Avalonia 的樣式回呼，在視窗屬性變動時維持目前模式的政策。
- 不變更錄影核心、教鞭、筆跡文件格式或復原／重做歷史。

## 測試範圍與限制

- 修正前：註記 UI／政策案例 3 項失敗、27 項通過，包含清除後表面重開與缺少 Windows 分層樣式。
- 修正後：註記相關 50 項通過。另以 `OPENCAM_REAL_UI_RENDER=1` 執行 Skia 註記 UI 測試，30 項通過；實際渲染影格確認有筆跡時與空白不同，清除後選橡皮擦與原始空白影格相同。
- 已加入 Windows user32 原生測試：反覆切換操作／繪圖，檢查實際 HWND 的政策、分層透明度與模式回復。本機為 macOS，該項跳過，不能視為 Windows 已通過。
- 完整 `dotnet test ScreenRecorder.sln -c Release -m:1 /nodeReuse:false`：Core 384 項通過；Media 1,642 項通過、28 項依平台／裝置條件跳過；失敗 0 項。建置有既有警告，並非零警告建置。
- 最終以真實 Skia 渲染重跑註記與 Windows 政策篩選案例：50 項通過、1 項 Windows 原生測試跳過、失敗 0 項；畫面比對操作實際工具列的「清除全部」與橡皮擦按鈕。
- Windows 桌面點擊穿透、筆跡是否持續顯示、錄入影片結果及多螢幕 DPI 仍須實機驗證。Skia Headless 測試不取代桌面合成器實測。

實機應覆蓋：有筆跡時操作底下不同程式（含點在筆跡上）、再切回任一繪圖工具；操作 → 清除全部 → 橡皮擦重複十次；復原清除；F10/F11 切換；暫停／繼續；雙螢幕不同縮放；最後播放錄影片段確認筆跡與清除時機。
