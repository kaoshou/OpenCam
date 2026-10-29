# Preflight Audio Balance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓使用者在錄影前平衡兩音源、試聽並記住音量，正式錄影沿用相同值。

**Architecture:** 擴充既有設定快照、預檢生命週期與 libobs 來源音量。來源健康監測獨立於音量；混音表以真實混音資料為準，不以來源峰值加總冒充。先驗證預檢與原生混音相容性，再整合介面。

**Tech Stack:** C#/.NET 8、Avalonia、既有 FFmpeg 音訊預檢、C++ libobs。

**Spec:** `docs/superpowers/specs/2026-10-29-preflight-audio-balance-design.md`

## 執行裁定

- 沿用目前獨立暫存 clone 的開發分支，不切換 master，不另建工作目錄。
- 音量滑桿停止變動 300ms 後，經既有 EndPreflight／BeginPreflight 確認停止及重啟，更新 operation 身分並失效舊試錄；不新增可與擷取並行修改的 IPC 命令。代價是套用時有短暫音訊重啟，而不是即時耳返。
- 非 100% 音量使用 helper 協定 2，舊 helper 在啟動前拒絕；100% 保持協定 1。原生 runtime 清單格式不變。
- 音訊確認視窗增加垂直空間並保留捲動；三語版面以 Skia 繪製核對。來源表在拖曳時估算衰減，總混音表等待新的實際量測。
- 配置、預檢與 helper 跨層需原子交付，待全部回歸通過後一起提交；不得提交可保存非預設音量但會被正式核心忽略的中間版本。
- Windows 原生編譯、雙系統實體裝置及五分鐘同步／聽感驗收仍須另行執行，不以合成測試取代，不因此推送或發布。

## Global Constraints

- 0～100%，預設 100%；只衰減、不放大、不改 OS 音量。
- 確認開始保存，取消不保存；不加入分軌或耳返。
- 保留 NativeOnly、MKV、健康檢查與資源清理，不新增錄影 fallback。
- 不升版、不推送、不發布；實機未驗證不得宣稱通過。
- 繁中、英文、日文完整，鍵盤可調滑桿且有可讀標籤。

## Review Focus

- 兩音源混音可能使用不同正規化規則：用已知振幅雙來源測試實際比例與總幅度。
- 0% 來源仍須初始化且可偵測裝置故障：測靜音與故障不同結果。
- 滑桿快速拖曳後關窗：不得有過期回覆覆蓋最新設定或殘留擷取。
- 舊 helper／舊 JSON：預設相容，非預設音量不得被不支援 helper 默默忽略。
- 已確認後暫停續錄及略過預檢：沿用同組倍率，不套兩次。

---

### Task 1: 音量契約與設定持久化

**Files:** `RecordingConfiguration.cs`、`UserSettings.cs`、`MainViewModel.cs`、`MainViewModel.RecordingContent.cs`、`MainWindow.axaml.cs`；測試 `RecordingAudioBalanceTests.cs`（Core）、`PreflightAudioBalancePreferenceTests.cs`（Media）。

**Interfaces:** `SystemAudioVolumePercent`、`MicrophoneVolumePercent` 為整數，缺值預設 100，setter 限制至 0～100；將百分比轉線性倍率僅一次。

- [ ] 寫失敗測試：預設 100、-1→0、101→100、JSON 缺值／往返、ApplyPausedSettings 保留倍率。
- [ ] 執行新增測試，確認缺少契約而失敗。
- [ ] 實作設定契約與主畫面建立配置／讀取／儲存／續錄傳遞。
- [ ] 寫並跑 UI 偏好測試：確認保存，取消與失敗回覆不保存，已有片段不被更改。
- [ ] 跑 Core 與設定相關回歸，審查差異後提交此階段。

### Task 2: 預檢混音、量測及設定修訂

**Files:** `FFmpegPreflightAudioCapture.cs`、兩平台 `*FFmpegProvider.cs` 及其共用音訊參數建構器、`RecordingPreflight.cs`、`PreflightMessages.cs`、`PreflightCoordinator.cs`、`PreflightIpcDispatcher.cs`、`PreflightAudioSession*.cs`；新增 `PreflightAudioBalanceTests.cs`。

**Interfaces:** 用現有配置 revision／operation 身分保護音量更新；擴充 `PreflightStatus` 提供可缺省的實測混音量及其 freshness，不把 unavailable 當成零。調音更新經獨立的受驗證音量命令處理，不允許此命令修改裝置或錄影參數。

- [ ] 寫失敗測試：100/25%、0/100%、單來源、已知兩頻率振幅、真混音峰值與無自動 normalize；兩平台相同。
- [ ] 執行測試確認失敗；確認目前 FFmpeg 混音規則與 OBS 實測差異後才修改。
- [ ] 在各來源混合前套線性倍率；加入混音後實測 RMS/peak，採有界量測資料且不記錄音訊內容。
- [ ] 實作序列化音量更新、合併快速拖曳更新、停止舊試聽、失效試錄／確認指紋；禁止試錄中改值。
- [ ] 測過期 revision、更新後回放拒絕、取消／關窗／端點故障清理及 0% 健康判定。
- [ ] 執行全部 Preflight 測試與合成混音解碼檢驗，提交。

### Task 3: 正式 OBS 錄影套用相同音量

**Files:** `ObsCaptureRequest.cs`、`ObsRuntime.cs`、`native/obs-capture/main.cpp`、對應原生協定測試及 `ObsAudioBalanceTests.cs`。

**Interfaces:** helper request 攜帶兩個百分比，宣告音量能力；每來源初始化後套用 `obs_source_set_volume`，不把音量 0 視為取消選取。

- [ ] 寫失敗測試：請求序列化、範圍驗證、舊請求缺值、舊 helper 的非 100% 請求必須明確拒絕。
- [ ] 跑測試確認失敗，再實作原生來源增益及能力檢查。
- [ ] 以合成雙音源驗證個別頻率振幅比例、總混音與預檢一致；涵蓋靜音、續錄、非 100% 與 100% 基準。
- [ ] 檢查來源 callback 健康值不受增益污染，畫面與時間戳路徑不改。
- [ ] 跑原生／managed 測試；Windows SDK 或 libobs 環境不足標記待驗證，提交。

### Task 4: 介面、多語系與交接驗證

**Files:** `RecordingPreflightWindow.axaml`／`.axaml.cs`、`RecordingPreflightViewModel.cs`、三語 `LocalizationService*.cs`；新增 `PreflightAudioBalanceUiTests.cs` 及 `docs/testing/preflight-audio-balance.md`。

**Interfaces:** 來源卡滑桿 0～100、百分比、調整後表、獨立健康狀態；混音總表／更新中、重設、重新試錄提示；確認回傳的 configuration 是最後已套用版本。

- [ ] 寫失敗測試：未選來源停用、0% 明確提示、滑桿鍵盤可操作、三語鍵完整、重設 100、舊試錄不可重播、確認不使用尚未套用值。
- [ ] 跑測試確認失敗，完成介面與字串；保留原有關閉與開始交接保護。
- [ ] 跑完整 solution tests、NativeOnly UI build、相關打包腳本測試，保存真實結果及跳過清單。
- [ ] 實機驗證 Windows/macOS 五秒試錄、播放、確認、取消、五分鐘含暫停續錄、MKV/MP4 音量及同步；受限項目列人工檢查步驟。
- [ ] 完成程式碼審查及驗收報告，交付改動與未驗證項目；未另獲授權不推送或發布。
