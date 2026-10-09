# 專案儲存／輸出工作流程驗證

日期：2026-10-09。開發分支 `codex/recording-project-persistence`，VERSION 0.2.7。未合併、未發布、未觸發 GitHub Actions。

## 本次行為

- 首頁專案區可選「停止錄影後自動輸出 MP4」，新／舊專案預設開啟；關閉時停止只保存錄製與專案。首頁和編輯器均可手動輸出。
- 剪輯、排序及改名只變更工作狀態；「儲存專案」才提交正式編輯。背景草稿只有中繼資料，沒有影片複本，也不改寫原始 MKV。
- 關閉編輯器／專案、切換、續錄與輸出前，未儲存內容提供儲存／不儲存／取消。返回首頁不卸載工作狀態。
- 有效未提交草稿在重開時詢問復原；復原後仍是未儲存。正式保存與捨棄標記防止舊草稿復活。
- 相同內容輸出前核對既有 MP4 的大小及 SHA256；通過才提供開啟既有檔案／另行輸出／取消。核對可取消，伺服器最長三秒；超時不信任舊檔，允許另行輸出，不覆寫既有檔。
- 左方縮圖／精簡清單可拖曳排序，沿用時間軸群組與復原規則；Escape 取消，一次拖曳一次復原。
- 編輯器標題「編輯錄製內容」，名稱後方為有框線與提示的鉛筆按鈕；首頁使用影片＋筆線條圖示。

## 自動化證據

- 審查修正後 Release 完整 solution：Core 205 通過；Media 635 通過、16 略過、1 失敗。包含自建音訊 helper，不啟用螢幕／麥克風擷取測試。
- 唯一失敗是另一份快速輸出計畫既有的 `ProjectConcatExportTests.CompleteSegmentsRetainOrderWithoutEncoding(count:100,audio:true)`：AAC 串接波形計數 114，期望 108–112。沒有放寬斷言；正式 `ConcatConvertAudio` 快速路徑尚未啟用，保留重新編碼回退。本報告不將快速輸出計畫視為完成。
- 53 項明確保存／IPC／輸出政策／UI 針對測試通過。
- `ChangingExportPolicyPreservesPendingDraftImmediately`：修改偏好後立即關閉會遺失待寫草稿，已以 RED→GREEN 修正為回覆前寫入替代草稿。
- `ClosingEditorPromptsButReturningHomeKeepsEdits`：X 關閉原先跳過確認，已 RED→GREEN 修正；`ReturnHomeKeepsWorkingEdits` 通過。
- `ProjectIpc_ClipListPointerDragCommitsOnceAndEscapeCancels`：兩種顯示模式均通過實際 pointer 事件測試。
- `RenameButtonRemainsBesideNameAndReachable`：中英文、長短名稱四組配置通過。
- `CancelExistingOutputCheckDoesNotExportOrInvalidateProject`：取消檔案核對不輸出、不使專案狀態變成未知。
- 網站測試 29/29 通過；沙箱初次執行因 Swift module cache 權限失敗，正常權限重跑通過，未修改測試。

## 實機限制

- 本次嘗試啟動新編譯 OpenCam 非錄影截圖模式，Avalonia.Native 在視窗建立前回報 `RenderTimer -6661`；無原生新版截圖。無頭配置／pointer 測試不等於原生視窗驗收。
- Windows 原生環境不在此工作階段：音訊裝置、長錄製、原生清單自動捲動、鍵盤焦點、縮放顯示及完整儲存／續錄／輸出流程仍需 Windows 實機驗收。
- 不宣稱所有剪輯、預覽或快速輸出工作已完成。此處只記錄本次儲存工作流程的可重現證據。

## 獨立審查修正

一次獨立唯讀審查找出三項 Important：無效草稿卡住正式專案開啟、輸出偏好提交與替代草稿之間的故障遺失復原依據，以及未寫成的草稿 ID 阻止捨棄。

四個故障注入案例先失敗，再修正通過。修正方式：

- 明確捕捉草稿驗證失敗，保留無效草稿到 `project.edits.invalid.json`（不覆寫舊診斷副本），正式專案仍可開啟並顯示警告。若診斷檔已占用或草稿無法安全移動，仍需手動保留／移動後才能恢復自動草稿。
- 偏好更新保存 `RecoveryDraftBaseRevision`，保留最後成功寫入草稿的基準；只有正式編輯保存才清除過渡標記，避免兩個檔案之間的失敗使草稿失效。
- 草稿 ID 只在寫入成功後承認，寫入失敗不會阻止「不儲存」。
- 將輸出後可選的 receipt 指紋作業也納入取消及三秒時間限制；取消這個附加步驟不會把已完成的 MP4 誤報為失敗。代價是本次沒有可供下次重用的 receipt。

審查修正針對性測試 7 項通過；完整回歸只有前述已知 AAC 失敗，未將它隱藏或略過。沒有再次派送 reviewer；本次修正以失敗重現測試及完整回歸驗證。

## 待完成

- 另一份快速輸出計畫的 100 段 AAC 串接失敗；本次不將整體開發版本標示為可發布。
- macOS 原生視窗、Windows 實機驗收及新版畫面截圖。
