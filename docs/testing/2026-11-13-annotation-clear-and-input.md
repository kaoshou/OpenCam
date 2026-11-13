# 螢幕註記：清除後殘影與操作模式

## 查核與變更

- 使用者回報：繪圖 → 操作 → 清除全部 → 橡皮擦，會閃現舊筆跡；操作模式有筆跡時仍阻擋底下程式。
- 已確認程式路徑：Canvas 呼叫 InvalidateVisual 後，Host 立即隱藏空白文件的視窗，下次選工具又 Show。修正為清除時更新同一個顯示表面，避免在空白影格完成前隱藏並重開舊合成緩衝區。此生命週期路徑已由回歸案例捕捉；本機未直接拍到 Windows 瞬間殘影。
- 發布前審查發現：Avalonia 11.2.5 的每個視窗類別使用 CS_OWNDC，不能加上 WS_EX_LAYERED。先前本機提交 a22dbe8 的分層方案未發布，已撤回；原先 hidden STATIC HWND 測試無法捕捉這個類別差異。
- Windows 實際桌面確認：單純 EnableWindow(false) 不足以讓點擊到達底下程序，該候選方案已撤回。最終採用相容的原生 STATIC 分層顯示視窗，只在操作模式顯示筆跡；原 Avalonia 繪圖視窗隱藏。切回繪圖時建立新 HWND，避免舊合成緩衝區殘影，使用同一份 Document 保留復原歷史。沒有更動錄影核心；macOS 與 X11 維持原顯示路徑。
- WindowFromPoint 與單純旗標測試不能證明跨程序滑鼠傳遞；因此新增獨立 Windows 桌面程式，使用實際 Avalonia HWND 與另一個程序接收 SendInput，並檢查筆跡像素、空白點擊、繪圖恢復及清除流程。CI 必須先通過此驗證，才可交付這個方案。

## 驗證紀錄

- 清除修正前：3 項 UI／政策案例失敗；原始版本 27 項通過。
- 發布前攔截不相容分層樣式的案例：1 項失敗，確認能攔截錯誤政策。
- 修正後本機 Skia 真實渲染與註記回歸：50 項通過、1 項 Windows 原生案例跳過；使用真正工具列清除及橡皮擦按鈕，比對清除前後的實際渲染影格。
- 前一提交完整解決方案：Core 384 通過；Media 1,642 通過、28 跳過；0 失敗。這項數字不取代本次 Windows 政策變更後的 CI 完整測試。
- Windows runner 真正桌面驗證已通過：[43380904558](https://github.com/kaoshou/OpenCam/actions/runs/43380904558)，提交 c084459。基準目標程序先收到 SendInput，再驗證筆跡／空白處跨程序點擊、筆跡維持可見、切回畫筆後的原生輸入及清除／橡皮擦。這是實際 Avalonia 桌面與獨立程序，不是 Headless 旗標測試。
- 新增有筆跡暫停續用、操作模式清除／復原／重做、原生視窗釋放及 150% 像素／DIP 轉換案例，將由最終 CI 驗證；縮放影像案例不取代真正的多螢幕 DPI 測試。不同實體硬體及錄入影片的主觀顯示仍需實測。

參考：[Avalonia 11.2.5 WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/11.2.5/src/Windows/Avalonia.Win32/WindowImpl.cs)、[Microsoft Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)、[EnableWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enablewindow)。
