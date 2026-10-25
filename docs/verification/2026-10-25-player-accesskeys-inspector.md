# 網頁定位與片段屬性即時編輯回歸

基底：`3805e4de395dbdd4362d737498690b1f5f1ad176`，版本維持 1.0.0。使用者後續要求 Windows 測試包，允許透過開發分支與獨立驗證流程建置；不合併主分支、不發布 Release。

後續補入錄影關閉攔截視窗按鈕水平／垂直置中，未改關閉攔截邏輯。三語、一般／狀態未確認兩種警告共 6 項新增排版測試由失敗轉通過；連同編輯器外觀回歸共 23 項通過。Windows CI 加入屬性編輯、警告視窗、編輯器外觀、網頁匯出與播放器腳本測試，測試包內附本輪驗收步驟。

## 修改與原因

- 播放器依通過驗證的字幕軌決定是否提供逐字稿：沒有字幕時隱藏頁籤、頁首跳轉入口與 T 快捷鍵說明；方向鍵及跳轉處理也不能選取隱藏頁籤。關閉 CC 或搜尋無結果不等同沒有字幕。
- 移除鍵盤說明內重複的三個超連結。原有頁首鍵盤定位連結保留，增加原生 HTML `accesskey`：C 播放器、L 章節、T 逐字稿。選單、搜尋輸入與滑桿不被影片單鍵快捷鍵攔截。
- 繁中、英文、日文同步說明各平台修飾鍵、Tab 替代導覽與逐字稿顯示條件。參考[數發部網站導覽](https://moda.gov.tw/sitemap/546)及 [MDN accesskey](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/accesskey)。不宣稱因此完成 WCAG 認證。
- 片段屬性舊流程每次自動儲存都設定全域 IsBusy，停用輸入框；每個 revision 又清空預覽與片段集合、重選片段並重設屬性。原有測試可重現預覽被清空。
- 新流程仍使用原來的版本檢查、IPC 序列化與 250ms 合併儲存，但屬性輸入框在自己的儲存期間保持可用。同一組片段就地替換，不清空集合；較舊的儲存回覆不回填覆蓋較新的輸入。失敗保留草稿及錯誤，不無限重試。
- 純名稱變更不清除影像、縮圖與波形快取；影像效果變更保留目前預覽直到新版影格驗證成功，再替換影像。換專案、換伺服器或模式變更仍使用原有失效與清除流程。

## 驗證與限制

- 播放器 Node 測試：38 通過。
- WebPlayerExportTests：7 通過，確認嵌入與輸出使用目前模板。
- 完整 Chromium 瀏覽器回歸：22 通過、1 明確略過、0 失敗，證據 `/tmp/opencam-accesskeys-verified-20261025/results.json`，包含三語、手機／平板／桌機重排、下載與離線操作、Tab／Enter 定位、Accesskey 宣告、無字幕入口、axe 檢測。macOS 的原生 Accesskey 不以 CDP 按鍵宣稱通過，另用桌面輸入實測。初次完整結果有 CDP 快捷鍵未觸發及一項舊測試仍嘗試點擊已隱藏逐字稿，修正測試的預期與覆蓋範圍後重跑。
- 本機 macOS Chrome 另用原生桌面輸入確認 C、L、T 的焦點到達正確區塊。這台 Mac 需要 Control + Option + Shift；只有 Control + Option 的首次自動化檢查未觸發，已補入多語說明並重跑通過。Windows／Firefox／Safari 原生按鍵尚未實機測試。
- 新增真實 Avalonia 控制項測試：屬性區顯示後，儲存期間輸入框不被停用、焦點與 caret 不遺失、後續輸入最終保存；另驗證預覽不被清空、效果影格仍更新、集合不 Reset、儲存拒絕後保留草稿並可修正。這些是 Headless UI 測試，不等同 Windows／macOS 中文輸入法組字實機驗收。
- 整個 .NET 解決方案：Core 358 通過、Media 1364 通過／23 略過，合計 1722 通過、23 略過、0 失敗，命令退出碼 0。Media 約 6 分 48 秒，包含本次 12 項屬性編輯測試；需要 Windows、實體裝置或明確環境授權的案例仍依原有規則略過，未算為通過。建置仍有既有 NU1900 套件弱點資料服務無法連線警告與 AVLN3001 預檢視窗建構子警告。
- 網站獨立測試：正常桌面權限下 38 通過、3 失敗。失敗為未在本次修改的既有版本／文件不一致：`project documentation reports only current, evidence-backed behavior`、`localized homepages include genuine previews, guide, and current download`、`canonical version is strict and drives packaging plus localized website content`。基底仍有 0.4.1 README／首頁，但測試預期 1.0.0；沒有為本次播放器修改擅自變更網站下載目標。初次沙箱執行另有 AppKit 圖示測試失敗；正常桌面權限重跑該項通過。
- 本次未改錄影核心、編碼、音訊或媒體匯出邏輯，未重做實體錄影耐久或 AV 同步驗收。

主要日誌：`/tmp/opencam-accesskeys-inspector-full.log`、`/tmp/opencam-accesskeys-browser-verified.log`、`/tmp/opencam-accesskeys-node-final.log`、`/tmp/opencam-accesskeys-export.log`、`/tmp/opencam-accesskeys-website-desktop.log`。
