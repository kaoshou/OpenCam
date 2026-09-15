# 高可靠跨平台螢幕錄影工具開發規格

## 1. 專案目標

請協助我從零開始規劃並開發一套「簡單、穩定、高可靠度」的桌面螢幕錄影工具。

本專案概念上類似 oCam 的「螢幕錄影」核心功能，但不需要複製 oCam 的全部功能，也不需要加入影片剪輯、直播、GIF、截圖編輯等額外功能。

本專案的核心目標只有：

> 讓使用者可以簡單選擇錄影範圍、系統聲音與麥克風，然後可靠地完成長時間螢幕錄影。

「錄影可靠性」的重要性高於 UI、功能數量與開發速度。

---

# 2. 主要需求

第一階段至少提供：

- 全螢幕錄影
- 指定螢幕錄影
- 自訂矩形區域錄影
- 可選擇是否錄製系統聲音
- 可選擇是否錄製麥克風
- 可選擇音訊裝置
- 30 FPS
- 60 FPS
- 開始錄影
- 停止錄影
- MKV 安全工作檔
- 正常停止後自動 Remux 為 MP4
- Crash Recovery
- 錄影 Session 狀態保存
- 磁碟剩餘空間監控
- Structured Logging
- 長時間穩定錄影

第一階段不需要：

- 影片剪輯
- GIF
- 直播
- YouTube 上傳
- Webcam Overlay
- OCR
- 即時特效
- 專業串流功能
- OBS 等級場景管理

---

# 3. 開發平台

第一階段主要支援：

- Windows 10
- Windows 11
- x64

但從第一天開始，程式架構必須考慮未來移植至：

- macOS

目前不要求立即完成 macOS 版本。

第一階段只需要完成 Windows 正式可用版本，但禁止將整個系統設計成 Windows-only architecture。

---

# 4. 建議技術棧

優先評估以下架構：

- C#
- .NET 8 或目前適合正式發布的 .NET LTS
- Avalonia UI
- MVVM
- Dependency Injection
- Structured Logging

Windows 螢幕擷取優先：

- Windows.Graphics.Capture
- Direct3D / Direct3D11，如有必要

Windows 音訊：

- WASAPI
- WASAPI Loopback
- 可評估 NAudio，但必須說明採用理由

影片編碼與封裝：

- FFmpeg

錄影中的主要安全容器：

- MKV

最終使用者輸出格式：

- MP4

正常停止錄影後：

```text
MKV → Remux → MP4
```

原則上禁止為了產生 MP4 而重新編碼。

如果經過技術分析後認為有更穩定、更適合長期維護的方案，可以提出調整，但必須先說明：

1. 為什麼修改
2. 修改後的優點
3. 對穩定性的影響
4. 對未來 macOS 移植的影響

不要只因為某個方案比較容易寫，就犧牲可靠性。

---

# 5. 最高優先原則：Recording Reliability

這是本專案最重要的規則。

任何功能、UI、效能或架構決策，都不得犧牲已錄製資料的安全性。

本軟體必須假設以下事件一定可能發生：

- UI crash
- Recorder crash
- FFmpeg crash
- 麥克風突然拔除
- 音訊裝置消失
- 預設音訊裝置改變
- 螢幕拔除
- 螢幕解析度改變
- DPI Scaling 改變
- Windows 鎖定
- Windows 睡眠
- Windows 喚醒
- 磁碟空間不足
- 磁碟寫入錯誤
- 使用者強制關閉程式
- 使用者從工作管理員 End Task
- 系統非正常關機
- 長時間錄影
- Encoder 異常
- Capture thread 異常

我們不能保證程式永遠不 crash。

真正的目標是：

> 即使程式異常，也應盡最大可能保留異常發生以前已成功錄製的內容。

---

# 6. 禁止直接以傳統 MP4 作為主要錄製工作檔

錄影進行中：

優先使用：

```text
recording_xxx.mkv
```

正常停止後：

1. 正常結束 MKV
2. 驗證 MKV 是否可讀
3. Remux 成 MP4
4. 驗證 MP4
5. 確認成功後才依設定處理 MKV

禁止：

```text
錄影開始
↓
recording.mp4
↓
所有 metadata 等到最後才 finalize
```

因為如果程式非正常中止，不應讓整段錄影因此全部損毀。

---

# 7. Recovery 機制

每一次 Recording Session 都應建立獨立工作目錄。

例如：

```text
Recordings/
└── Sessions/
    └── 20260915_004500_xxxxx/
        ├── session.json
        ├── recording.mkv
        ├── recording.log
        └── recovery.json
```

`session.json` 至少應保存：

- Session ID
- 開始時間
- 最後狀態
- Capture source
- Capture region
- Monitor
- Resolution
- FPS
- Audio configuration
- System audio device
- Microphone device
- Encoder
- Output path
- Temporary path
- Recording state

程式啟動時應檢查是否存在：

- Recording
- Interrupted
- Recoverable

等未正常結束的 Session。

如果存在，應提供 Recovery。

例如：

```text
偵測到上次未正常完成的錄影

開始時間：
已錄製時間：
檔案大小：
工作檔位置：

[恢復]
[轉換成 MP4]
[開啟檔案位置]
[保留原始檔]
```

Recovery 過程不得先刪除原始 MKV。

---

# 8. Recorder 與 UI 解耦

請優先評估：

```text
UI Process
```

與：

```text
Recorder Process
```

是否應分離。

理想狀況：

```text
ScreenRecorder.UI

ScreenRecorder.Recorder
```

分開。

UI crash 不應必然導致 Recorder crash。

如果採用 Process Separation，請設計：

- IPC
- Recorder state
- Heartbeat
- Graceful shutdown
- Unexpected disconnect
- Recorder recovery

IPC 可評估：

- Named Pipe
- Local IPC
- 其他適合 .NET Desktop 的方式

請選擇簡單、可靠、容易維護的方式。

不要為了架構漂亮而過度工程化。

---

# 9. Watchdog

評估加入 Watchdog / Health Monitoring。

至少監控：

- Capture 是否仍有 frame
- Encoder 是否仍運作
- Output file 是否持續增加
- Audio capture 是否正常
- Recorder process 是否 alive
- 剩餘磁碟空間

如果發現異常：

優先：

1. 保留現有錄影
2. 嘗試安全停止
3. Flush 可以保存的資料
4. 更新 Session 狀態
5. 寫入 Log
6. 通知 UI

不得因某個非必要功能失敗而讓整個 Recorder crash。

---

# 10. 第一階段核心 Capture 功能

Windows 第一版必須至少支援：

- 全螢幕錄影
- 指定螢幕錄影
- 自訂矩形區域錄影

未來可增加：

- 指定視窗錄影

---

# 11. 自訂區域選擇

使用者點：

```text
選擇錄影區域
```

後：

顯示螢幕 Overlay。

使用者可以：

- 滑鼠拖曳選擇矩形
- 顯示寬 × 高
- 調整選取範圍
- 確認
- 取消

必須正確處理：

- 多螢幕
- 不同 DPI
- Windows Scaling
- Negative monitor coordinates

錄影開始後，區域選擇 Overlay 必須消失。

---

# 12. 音訊

至少支援：

系統聲音：

- 開
- 關

麥克風：

- 開
- 關

並允許使用者選擇：

- System audio device
- Microphone device

至少應支援四種組合：

1. 無聲音
2. 只有系統聲音
3. 只有麥克風
4. 系統聲音 + 麥克風

---

# 13. 音訊裝置異常

如果錄影過程中發生：

- 麥克風被拔除
- Audio device lost
- Default device changed

不得直接讓整個 Recorder crash。

應：

- 記錄錯誤
- 儘可能繼續畫面錄製
- UI 顯示警告

如果技術上可行，可嘗試重新連線。

但：

> Recovery 不得造成比原本錯誤更大的風險。

---

# 14. Audio / Video Synchronization

必須特別處理長時間錄影：

- Audio drift
- Video timestamp
- Dropped frames
- Variable frame timing
- System load

不要假設：

每秒一定剛好收到 30 或 60 frames。

Timestamp 必須有可靠來源。

請設計長時間 A/V sync 測試。

至少測試：

- 30 分鐘
- 1 小時
- 2 小時
- 4 小時

後續穩定版本應測試：

- 8 小時

---

# 15. FPS

第一版提供：

- 30 FPS
- 60 FPS

預設：

```text
30 FPS
```

如果裝置或系統無法穩定維持 FPS：

不應造成 Recorder crash。

---

# 16. Encoder

優先支援 H.264。

請設計 Encoder abstraction，例如：

```text
IEncoder
```

未來可以支援：

- Software H.264
- NVIDIA NVENC
- Intel Quick Sync
- AMD AMF
- Apple VideoToolbox

第一版不要求全部完成。

第一版可以先選擇最穩定方案。

但 Core 不得寫死單一 encoder。

---

# 17. Hardware Encoder

後續版本可自動偵測：

- NVIDIA
- Intel
- AMD

如果 Hardware Encoder 不可用：

自動 fallback。

Fallback 不得造成錄影中止。

第一版可以先不做完整 Hardware Encoder 自動選擇，但架構必須保留。

---

# 18. Disk Space Monitoring

錄影前：

檢查目標磁碟剩餘空間。

錄影中：

定期檢查。

例如設定：

```text
Warning threshold
```

與：

```text
Critical threshold
```

當剩餘空間過低：

UI 必須明確警告。

到 Critical threshold 時：

應考慮：

```text
安全停止錄影
```

而不是：

```text
讓磁碟完全寫滿後 crash
```

所有 threshold 應可設定。

---

# 19. 檔案命名

預設：

```text
Recording_yyyyMMdd_HHmmss.mkv
```

完成：

```text
Recording_yyyyMMdd_HHmmss.mp4
```

避免檔名衝突。

---

# 20. Remux

正常停止後：

```text
MKV → MP4
```

應使用 Stream Copy。

例如概念：

```text
-c copy
```

不要重新 encode。

Remux 完成後必須：

- 檢查 exit code
- 檢查 MP4 是否存在
- 檢查檔案大小
- 最好使用 ffprobe 或等效方式驗證

只有驗證成功：

才能依設定刪除 MKV。

如果 Remux 失敗：

保留 MKV。

---

# 21. UI 設計

UI 目標：

簡單。

不要設計成 OBS。

主畫面應盡量讓一般使用者一看就會。

大致包含：

## 錄影範圍

- 全螢幕
- 指定螢幕
- 自訂區域

## 音訊

- 系統聲音
- 麥克風

## 裝置

- System Audio
- Microphone

## 品質

- FPS
- Quality

## 儲存位置

以及：

```text
開始錄影
```

錄影中：

顯示：

- Recording
- Duration
- File size
- Disk remaining
- Audio status

提供：

- Pause，後續可實作
- Stop

---

# 22. 系統列

後續可支援 System Tray。

錄影時：

程式最小化仍能正常錄影。

Tray 顯示：

- Recording
- Duration
- Stop Recording

但第一階段不要因 Tray 功能拖慢核心 Recorder。

---

# 23. Hotkey

後續支援 Global Hotkey。

例如：

- 開始 / 停止錄影
- Pause / Resume

快捷鍵應可設定。

但不是第一版最高優先功能。

---

# 24. Logging

使用 Structured Logging。

例如：

- Serilog
- 或其他成熟方案

Log 至少記錄：

- Application start
- Recorder start
- Session ID
- Capture configuration
- Audio configuration
- Encoder
- FFmpeg command / sanitized arguments
- Device lost
- Capture error
- Encoder error
- Disk warning
- Stop reason
- Recovery
- Remux
- Unexpected exception

Log 不得記錄敏感資訊。

---

# 25. Exception Handling

禁止大量使用：

```csharp
catch
{
}
```

所有 Exception 必須：

- 有合理處理
- 或 Log
- 或往上傳遞

不得 Silent Failure。

同時：

非關鍵模組錯誤不應直接殺死 Recorder。

---

# 26. Crash Handling

應設計：

- Global exception handler

但不要誤以為 Global Exception Handler 可以解決所有 crash。

最重要的仍然是：

- Process isolation
- Continuous file writing
- MKV
- Recovery
- State persistence

---

# 27. macOS 未來架構

未來 macOS 預計：

Video：

- ScreenCaptureKit

Audio：

- Apple 官方適合的 Audio API
- 或 ScreenCaptureKit 音訊能力

Hardware Encoder：

- VideoToolbox

Permissions：

- Screen Recording
- Microphone

請從現在就確保 Core 不直接依賴 Windows。

---

# 28. 專案分層

建議至少：

```text
ScreenRecorder.sln

src/

ScreenRecorder.Core

ScreenRecorder.UI

ScreenRecorder.Recorder

ScreenRecorder.Platform.Windows

ScreenRecorder.Media

ScreenRecorder.Infrastructure

tests/

ScreenRecorder.Core.Tests

ScreenRecorder.Recorder.Tests

ScreenRecorder.IntegrationTests
```

實際結構可以經過分析後調整。

但必須維持：

```text
Core
↓
Platform abstraction
↓
Windows implementation
```

不得讓 Core 反向依賴 Windows implementation。

---

# 29. 建議 Interfaces

可評估：

```text
IVideoCaptureService

IAudioCaptureService

IRecordingService

IEncoder

IMuxer

IRecordingRecoveryService

IStorageService

IDiskSpaceMonitor

IDeviceEnumerator

IRecordingSessionStore

IRecorderHealthMonitor
```

具體 interface 請依實際架構設計。

不要為了 Interface 而 Interface。

---

# 30. Recording State Machine

請明確建立錄影 State Machine。

例如：

```text
Idle
↓
Preparing
↓
Recording
↓
Pausing
↓
Paused
↓
Stopping
↓
Finalizing
↓
Completed
```

異常：

```text
Interrupted

Recoverable

Failed
```

不得單純依靠大量 bool：

```text
isRecording
isStopping
isPaused
isError
```

避免產生 Race Condition。

---

# 31. Thread Safety

Capture、Audio、Encoder、UI 都可能涉及不同 thread。

必須特別注意：

- Race condition
- Deadlock
- CancellationToken
- Dispose
- Async lifecycle
- Process shutdown

禁止使用：

```text
Thread.Abort
```

應使用：

- CancellationToken
- 明確生命週期管理

---

# 32. Resource Management

所有：

- Stream
- COM object
- Capture session
- Audio device
- Direct3D resource
- FFmpeg process
- File handle

必須正確 Dispose。

長時間錄影必須監控：

- RAM
- Handles
- Threads
- GPU memory

不得隨錄影時間持續無限制增加。

---

# 33. Performance 優先順序

目標不是追求極限效能。

優先順序：

1. Recording reliability
2. Data safety
3. A/V synchronization
4. Stability
5. Reasonable CPU/GPU usage
6. UI responsiveness
7. Feature richness

如果效能最佳化會增加 crash risk：

優先穩定。

---

# 34. 測試要求

本專案不能只測：

```text
按開始可以錄影
```

必須建立 Fault Injection / Stress Test 思維。

至少規劃：

## Test 1

錄影 30 分鐘正常停止。

確認：

- MKV
- MP4
- Audio
- Duration

---

## Test 2

錄影中強制結束 UI。

確認：

- Recorder 是否繼續

---

## Test 3

錄影中強制結束 Recorder。

重新啟動程式。

確認：

- MKV 是否仍可播放
- Recovery 是否能找到

---

## Test 4

錄影中拔除麥克風。

確認：

- 畫面錄影是否繼續

---

## Test 5

磁碟空間不足。

確認：

- 是否安全停止

---

## Test 6

多螢幕。

---

## Test 7

不同 DPI Scaling：

- 100%
- 125%
- 150%
- 200%

---

## Test 8

長時間：

- 2 小時
- 4 小時
- 8 小時

---

## Test 9

Windows Lock / Unlock。

---

## Test 10

Sleep / Resume。

---

## Test 11

大量 CPU Load。

---

## Test 12

大量 GPU Load。

---

## Test 13

Remux 中途失敗。

確認：

- MKV 不會被刪除

---

# 35. 自動化測試

Core logic 應盡量 Unit Test。

例如：

- Recording State Machine
- Session persistence
- Recovery detection
- File naming
- Disk threshold
- Configuration
- Remux decision
- State transition
- Failure handling

Platform Capture 不需要硬做不切實際的 Unit Test。

對硬體與 OS 行為：

優先使用 Integration Test 與 Manual Acceptance Test。

---

# 36. Milestone 1：專案架構完成條件

必須滿足：

- Solution 可以在乾淨環境 restore
- Solution 可以成功 build
- Release configuration 可以成功 build
- 不得存在 build error
- 不應存在未處理的重要 compiler warning
- Core 不得直接引用 Windows-specific API
- Windows-specific implementation 必須位於 Platform.Windows 或等效專案
- UI 不得直接實作核心錄影邏輯
- Recording state machine 已建立
- Logging infrastructure 已建立
- Dependency Injection 已建立
- 基本 Unit Test project 已建立

驗收命令必須實際執行，例如：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

只有全部成功才可通過。

---

# 37. Milestone 2：基本畫面錄製完成條件

至少完成：

- 全螢幕錄影
- 指定螢幕錄影
- 自訂矩形區域錄影
- 30 FPS
- 60 FPS
- 開始錄影
- 停止錄影
- MKV 工作檔

每項功能必須實際測試。

## Test A

錄製：

```text
5 分鐘
1080p
30 FPS
```

確認：

- 可正常開始
- 可正常停止
- MKV 可播放
- Duration 誤差不得明顯異常
- 不得只有黑畫面
- 不得明顯卡死
- Recorder 不得 crash

## Test B

錄製：

```text
5 分鐘
1080p
60 FPS
```

同樣必須通過。

## Test C

自訂區域錄製。

必須確認：

- 實際輸出範圍與使用者選取範圍一致
- 不得明顯位移
- DPI scaling 下座標仍正確

---

# 38. Milestone 3：音訊錄製完成條件

以下四種模式都必須獨立測試：

1. 無聲音
2. 只有系統聲音
3. 只有麥克風
4. 系統聲音 + 麥克風

每項至少錄製 5 分鐘。

驗收：

- 設定為關閉的來源不得意外錄入
- 系統聲音清晰
- 麥克風聲音清晰
- 不得持續爆音
- 不得產生明顯斷裂
- Audio stream duration 應與 Video stream 接近
- 不得因音訊來源失敗造成整個 Recorder crash

---

# 39. Milestone 4：A/V Sync 完成條件

必須進行長時間同步測試。

至少：

- 30 分鐘
- 1 小時
- 2 小時

正式穩定版：

- 4 小時
- 8 小時

測試方式：

在影片中定期製造可同時看見與聽見的事件，例如：

- 畫面顯示計時器
- 每分鐘產生一次聲音
- 可使用 clap
- beep
- visual marker

驗收：

開始、中段、結尾都檢查 Audio / Video Sync。

建議目標：

- 2 小時錄影後 A/V 偏移應控制在約 ±100 ms 內
- 如果實際架構無法可靠達到此值，必須量測並說明
- 不接受隨錄影時間持續線性漂移

「肉眼看起來差不多」不能作為唯一驗收方式。

---

# 40. Milestone 5：MKV → MP4 完成條件

正常停止錄影後：

必須自動執行：

```text
MKV → MP4 Remux
```

驗收：

- 不得重新編碼
- Remux 成功
- MP4 可以播放
- Duration 正確
- Video stream 存在
- 預期 Audio stream 存在
- 原始畫質不得改變
- 原始 FPS 不得因 Remux 改變

應使用：

```text
ffprobe
```

或等效工具檢查輸出。

只有在 MP4 驗證成功後：

才允許依設定刪除 MKV。

---

# 41. Milestone 6：Crash Recovery 完成條件

這是核心驗收項目。

## Scenario A：Recorder 被強制結束

步驟：

1. 開始錄影
2. 持續至少 10 分鐘
3. 直接從 Task Manager 強制結束 Recorder process
4. 重新啟動程式

必須確認：

- 已錄製內容不是 0 byte
- MKV 仍可讀取或可恢復
- 至少保留 crash 前大部分已寫入內容
- 程式能辨識 interrupted session
- Recovery UI 能顯示該 session

---

## Scenario B：整個程式被強制結束

步驟：

1. 開始錄影
2. 錄製至少 10 分鐘
3. 強制結束所有 ScreenRecorder processes
4. 重新啟動

必須確認：

- Session 可被偵測
- 已錄內容沒有因缺少正常 Stop 而整段消失

---

## Scenario C：模擬非正常關機

如果自動測試環境不適合直接斷電，可使用接近實際 crash 的 Fault Injection。

必須驗證：

> 非正常結束不能使已錄數十分鐘內容全部失效。

這一條如果未通過：

不得將產品標示為 Stable。

---

# 42. Milestone 7：UI Process Isolation 完成條件

如果採用 UI / Recorder 分離：

測試：

1. 開始錄影
2. 錄製 5 分鐘
3. 強制結束 UI process

驗收：

- Recorder process 應繼續錄影
- 錄影檔仍持續增加
- UI 重啟後應能重新取得 Recorder 狀態，或至少提供明確 Recovery 流程

如果設計決定不採用 Process Isolation：

必須在 Architecture Decision Record 中清楚解釋原因。

---

# 43. Milestone 8：麥克風拔除完成條件

步驟：

1. 開啟 System Audio + Microphone
2. 開始錄影
3. 錄影途中拔除麥克風

驗收：

- Recorder 不得 crash
- Video 應繼續錄製
- System Audio 應盡可能繼續
- UI 顯示 Audio Device Lost
- Log 記錄 device loss
- Session 不得因此整段報廢

重新插入麥克風是否自動恢復：

可列為後續功能。

但不得因未實作自動恢復而造成 crash。

---

# 44. Milestone 9：系統音訊裝置異常完成條件

測試：

- 切換 Windows default audio device
- 停用目前播放裝置
- 移除 USB Audio Device

驗收：

- Recorder 不得直接 crash
- 畫面錄製應盡可能繼續
- UI 應顯示狀態
- Log 必須包含錯誤資訊

---

# 45. Milestone 10：磁碟空間不足完成條件

建立可控制容量的測試環境。

測試：

1. 開始錄影
2. 讓剩餘空間逐漸下降
3. 觸發 Warning threshold
4. 觸發 Critical threshold

驗收：

Warning：

- UI 有明確警告
- Recording 可繼續

Critical：

- 必須嘗試安全停止
- 不得一直寫到完全 0 bytes remaining
- 應 finalize 可保存資料
- Session state 正確
- 不得因 IOException 造成整段錄影遺失

---

# 46. Milestone 11：長時間穩定性完成條件

正式 Stable 版本之前，至少完成：

- 2 小時錄影
- 4 小時錄影
- 8 小時錄影

監測：

- Process RAM
- Working Set
- Private Bytes
- Handle count
- Thread count
- GPU memory
- CPU usage
- File size
- Dropped frames

驗收：

不得出現：

- RAM 持續無限制成長
- Handle 持續無限制增加
- Thread 持續無限制增加
- 明顯 Resource leak
- 最後無法 Stop
- Stop 後 FFmpeg process 殘留
- 輸出檔無法播放

如果存在長期上升趨勢：

必須視為 Bug。

---

# 47. Milestone 12：多螢幕完成條件

至少測試：

- 單螢幕
- 雙螢幕
- 不同解析度雙螢幕
- 主螢幕位於右側
- 主螢幕位於左側
- Secondary monitor 使用 negative coordinates

驗收：

- Monitor 選擇正確
- Capture region 正確
- 不得錄錯螢幕
- 自訂區域座標正確

---

# 48. Milestone 13：DPI Scaling 完成條件

至少測試：

- 100%
- 125%
- 150%
- 200%

如果多螢幕可用：

額外測試不同螢幕使用不同 DPI scaling。

驗收：

- Region selector 座標正確
- Capture output 不得明顯偏移
- Width / Height 顯示正確
- UI 不得因 DPI 發生不可用問題

---

# 49. Milestone 14：Display Change 完成條件

錄影途中：

- 修改解析度
- 改變 Scaling
- 拔除次要螢幕

驗收：

不得 Silent Crash。

如果目前無法安全繼續 Capture：

允許安全停止。

但必須：

- 保留已錄資料
- 顯示明確錯誤
- Session 狀態正確

---

# 50. Milestone 15：Lock / Unlock 完成條件

Windows：

1. 開始錄影
2. Win + L
3. 等待
4. Unlock

必須確認：

- 程式不 crash
- 行為明確
- 已錄內容安全

如果 Windows API 無法錄製 Lock Screen：

可以接受。

但軟體必須明確處理這種狀態。

---

# 51. Milestone 16：Sleep / Resume 完成條件

步驟：

1. 開始錄影
2. 系統進入 Sleep
3. Resume

可接受的策略：

A. Resume 後繼續

或：

B. Sleep 前安全停止

但不能：

- crash
- 留下完全無法恢復的錄影
- UI 顯示 Recording 但實際沒有在錄

必須明確定義產品行為。

---

# 52. Milestone 17：高負載測試

測試情境：

- CPU 高負載
- GPU 高負載
- Memory pressure
- 同時播放高解析度影片

驗收：

- Recorder 不得直接 crash
- 可以 drop frame，但必須統計
- Timestamp 不得因此完全失控
- Audio 不得長時間永久失步

UI 應盡可能保持可操作。

---

# 53. Milestone 18：FFmpeg 異常完成條件

如果採用 FFmpeg process：

測試：

錄影途中直接 Kill FFmpeg。

驗收：

- Recorder 能偵測 encoder/process 已死亡
- 不得假裝仍在正常 Recording
- UI 顯示錯誤
- Session 標記 Interrupted / Failed
- 已經寫出的檔案必須保留
- Recovery 可處理殘留檔案

---

# 54. Milestone 19：Remux Failure 完成條件

測試：

刻意讓 Remux 失敗。

例如：

- Output path 無權限
- Output disk full
- FFmpeg 返回非 0 exit code

驗收：

- 原始 MKV 必須保留
- 不得刪除原始 MKV
- UI 顯示 Remux failed
- Log 保存原因
- 使用者可以之後重試

---

# 55. Milestone 20：檔案完整性驗收

所有正式測試輸出檔應自動使用：

```text
ffprobe
```

或等效工具驗證。

至少取得：

- container format
- duration
- video codec
- video width
- video height
- frame rate
- audio codec
- audio sample rate
- stream count

驗收結果應寫成 Machine-readable report。

例如：

```text
artifacts/
└── verification/
    ├── test-001.json
    ├── test-002.json
    └── summary.json
```

---

# 56. Automated Acceptance Test Report

Codex 每完成一個 Milestone：

必須產生驗收報告。

例如：

```text
ACCEPTANCE_REPORT.md
```

格式至少包含：

```text
## Environment

OS:
Windows Version:
CPU:
GPU:
RAM:
Audio Device:
Display Configuration:
Application Version:
Commit:

## Tests

Test ID:
Description:
Result:

PASS / FAIL / BLOCKED

Evidence:
Log:
Output File:
ffprobe Result:
Notes:
```

不得只寫：

```text
Test passed.
```

必須提供足以重現與審查的 Evidence。

---

# 57. Definition of Done

單一功能只有同時符合以下條件才算 Done：

- Code 已完成
- Build 通過
- Relevant tests 通過
- Error handling 已實作
- Logging 已實作
- Resource cleanup 已處理
- 沒有已知 Critical Bug
- 沒有已知 Data Loss Bug
- 文件已更新
- Acceptance Test 已完成

只寫完 Code：

不算 Done。

只 Build：

不算 Done。

人工測一次：

不算 Done。

---

# 58. Stable Release Definition

只有全部符合以下條件，版本才可以標示為 Stable：

## Functional

- Full Screen Recording PASS
- Monitor Recording PASS
- Region Recording PASS
- System Audio PASS
- Microphone PASS
- System + Microphone PASS
- MKV PASS
- MP4 Remux PASS

## Reliability

- Forced Recorder Kill Recovery PASS
- Forced UI Kill PASS
- Audio Device Loss PASS
- Disk Full Protection PASS
- Remux Failure Protection PASS
- 4 Hour Recording PASS

正式推薦使用前：

- 8 Hour Recording PASS

## Platform

- Multi-monitor PASS
- DPI 100% PASS
- DPI 125% PASS
- DPI 150% PASS
- DPI 200% PASS

## Resource

- 無明顯 Memory Leak
- 無明顯 Handle Leak
- 無明顯 Thread Leak

## Data Safety

任何已知會造成：

```text
整段已錄內容全部遺失
```

的 Bug：

數量必須為：

```text
0
```

這是 Stable Release 的硬性條件。

---

# 59. Bug Severity

使用以下 Severity：

## S0 — Data Loss

例如：

- Crash 後整段影片消失
- Stop 後檔案完全損毀
- Recovery 刪除唯一可用的原始錄影

S0 必須立即處理。

存在任何已知 S0：

禁止 Release。

---

## S1 — Recording Failure

例如：

- Recorder crash
- Encoder crash
- Audio 導致錄影停止
- 長時間錄製必定失敗

存在 S1：

禁止 Stable Release。

---

## S2 — Major

例如：

- 特定 DPI 下區域偏移
- 某音訊裝置無法使用
- Remux 有特定情境失敗

可以進入 Beta，但必須記錄。

---

## S3 — Minor

例如：

- UI 顯示問題
- 非核心設定問題
- 不影響錄影安全的小 Bug

可依優先度排程。

---

# 60. Release Gate

每次準備 Release 時：

必須執行 Release Gate。

至少包含：

```bash
dotnet restore

dotnet build -c Release

dotnet test -c Release
```

以及：

- Integration Tests
- Acceptance Tests
- 5 分鐘 Video-only recording
- 5 分鐘 System-audio recording
- 5 分鐘 Microphone recording
- 5 分鐘 System + Microphone recording
- Forced Kill Recovery Test
- Remux Verification

如果任何 Critical Test Failed：

禁止 Release。

---

# 61. 不得假造測試結果

非常重要：

Codex 只能把「實際執行過」的測試標示為 PASS。

如果因為目前環境：

- 沒有 Windows GUI
- 沒有實際 Audio Device
- 沒有 Multiple Monitor
- 無法進入 Sleep
- 無法拔除 USB microphone

導致無法測試：

請標示：

```text
BLOCKED
```

或：

```text
REQUIRES MANUAL VALIDATION
```

不得寫成 PASS。

並產生：

```text
MANUAL_TEST_CHECKLIST.md
```

讓人類可以依步驟測試。

---

# 62. Manual Test Checklist

每個不能自動化的測試：

必須包含：

- Prerequisites
- Steps
- Expected Result
- Failure Condition
- Evidence to Collect

例如：

```text
## TEST-AUDIO-DEVICE-LOSS

Prerequisites:

- USB Microphone
- ScreenRecorder running

Steps:

1. 選擇 USB microphone
2. 開始錄影
3. 等待 60 秒
4. 拔除 USB microphone
5. 再錄製 120 秒
6. Stop

Expected:

- Recorder does not crash
- Video continues
- Warning appears
- MKV remains playable

Failure:

- Recorder exits
- MKV unusable
- UI freezes permanently

Evidence:

- recording.mkv
- application.log
- session.json
```

---

# 63. 每個 Milestone 的交付物

Codex 每完成一個 Milestone：

至少交付：

1. Source Code
2. Tests
3. Build Result
4. Acceptance Result
5. Known Issues
6. Relevant Logs
7. Architecture changes
8. Updated Documentation

不要只回覆：

```text
已完成
```

請明確列出：

```text
Completed:
Tested:
Not Tested:
Known Issues:
Next Recommended Step:
```

---

# 64. 最終產品驗收原則

本產品最重要的驗收問題不是：

> 功能有沒有很多？

而是：

> 我是否敢用它錄製一場無法重新錄製的 2 小時內容？

只有當答案可以合理地回答：

```text
是
```

才能將本產品視為達到正式可用標準。

---

# 65. 開發執行方式

不要一次實作全部功能。

請按照 Milestone 分階段完成。

每一階段：

1. 先分析
2. 寫 implementation plan
3. 實作
4. Build
5. Test
6. 驗收
7. 修正問題
8. 再進下一階段

前一階段如果存在：

- S0
- S1

不得直接繼續堆疊下一階段功能。

優先修復可靠性問題。

---

# 66. Architecture Decision Records

所有重要架構決策應建立 ADR。

至少包含：

- 為何採用 Avalonia
- 為何採用 Windows.Graphics.Capture
- 為何採用 MKV
- 為何使用 FFmpeg
- UI / Recorder 是否分離
- IPC 選擇
- Encoder abstraction
- macOS abstraction
- Recovery Strategy
- Audio Capture Strategy

ADR 格式建議：

```text
docs/adr/

0001-use-avalonia.md
0002-use-mkv-as-working-container.md
0003-recorder-process-isolation.md
...
```

每份 ADR 至少包含：

- Context
- Decision
- Alternatives
- Consequences
- Risks

---

# 67. 文件要求

專案至少維護：

```text
README.md

AGENTS.md

ARCHITECTURE.md

ROADMAP.md

TESTING.md

RELIABILITY.md

MANUAL_TEST_CHECKLIST.md

ACCEPTANCE_REPORT.md
```

後續視需要加入：

```text
TROUBLESHOOTING.md

RELEASE.md

SECURITY.md
```

---

# 68. AGENTS.md 開發原則

AGENTS.md 至少應包含：

## Coding Principles

- 優先清晰與可維護性
- 不做不必要的抽象
- 不做不必要的微最佳化
- 不使用 Silent Failure
- 所有 async lifecycle 要可取消
- 所有資源必須明確釋放
- 不允許 UI 直接操控底層 Capture implementation
- 不允許 Core 依賴 Windows implementation

## Reliability Principles

- Reliability before features
- Data safety before convenience
- Recovery before elegance
- Evidence before claiming completion

## Testing Principles

- Never claim PASS without execution evidence
- Hardware tests must be manual if environment does not support them
- Every S0 bug must block release
- Every S1 bug must block Stable release

---

# 69. Windows → macOS 抽象界線

以下功能應盡量維持跨平台：

- UI
- ViewModel
- Recording configuration
- Recording session
- State machine
- Logging
- Recovery
- Storage management
- Recording history
- Remux
- File verification
- Output naming
- Disk threshold logic
- Health monitoring orchestration

以下功能允許 OS-specific：

- Screen capture
- Audio capture
- Device enumeration
- Permissions
- Hardware encoder detection
- OS notification
- Global hotkey
- System tray integration
- Sleep / power event integration

Windows implementation：

```text
Windows.Graphics.Capture
WASAPI
Windows power events
Windows device enumeration
```

macOS implementation：

```text
ScreenCaptureKit
CoreAudio / Apple audio APIs
macOS permissions
VideoToolbox
```

Core project MUST NOT directly reference Windows-specific APIs.

---

# 70. 不可接受的設計

禁止以下做法：

- UI code-behind 直接處理所有錄影流程
- Core 直接引用 Windows.Graphics.Capture
- 所有狀態靠 bool 管理
- 所有錄影直接輸出傳統 MP4
- Stop 時才第一次寫檔
- 大量資料長時間只留在 RAM
- catch exception 後什麼都不做
- FFmpeg crash 後 UI 仍顯示 Recording
- Recovery 成功前刪除唯一原始檔
- Remux 成功前刪除 MKV
- 用 Thread.Abort
- 未 Dispose Capture / Audio / FFmpeg / Stream
- 未實測就宣稱 PASS
- 為了 UI 功能犧牲 Recorder 穩定性
- 為了方便開發取消 Crash Recovery

---

# 71. 第一版 UI 建議

第一版介面保持簡單。

例如：

```text
┌───────────────────────────────┐
│        Screen Recorder        │
├───────────────────────────────┤
│                               │
│ 錄影範圍                      │
│ ○ 全螢幕                      │
│ ○ 指定螢幕   [ Display 1 ▼ ] │
│ ● 自訂區域   [ 選擇區域 ]    │
│                               │
│ 系統聲音      ☑               │
│ 裝置          [ Default ▼ ]   │
│                               │
│ 麥克風        ☑               │
│ 裝置          [ Microphone ▼ ]│
│                               │
│ FPS           [ 30 ▼ ]        │
│                               │
│ 儲存位置                      │
│ [ D:\Videos\             ] [...]│
│                               │
│             [ ● 開始錄影 ]    │
└───────────────────────────────┘
```

錄影中：

```text
● Recording

00:35:27

File Size:
2.8 GB

Disk Remaining:
312 GB

System Audio:
OK

Microphone:
OK

[ Stop Recording ]
```

不要第一版就設計大量設定頁。

---

# 72. 錄影中狀態資料

Recorder 應提供至少：

- Session ID
- Recording state
- Elapsed time
- Output file
- Working file
- Current file size
- Available disk space
- FPS
- Dropped frames
- Video capture health
- System audio health
- Microphone health
- Encoder health
- Last error

UI 不應自己猜測 Recorder 狀態。

---

# 73. Stop 流程

Stop 不得只是：

```text
Kill FFmpeg
```

正確概念應是：

```text
User presses Stop
↓
State = Stopping
↓
Stop accepting new capture
↓
Finish buffered frame/audio
↓
Request encoder graceful shutdown
↓
Finalize MKV
↓
Verify MKV
↓
State = Finalizing
↓
Remux MKV → MP4
↓
Verify MP4
↓
State = Completed
```

任何階段失敗：

都必須保留能保存的資料。

---

# 74. Application Close 行為

如果使用者在錄影中關閉 UI：

不可以直接：

```text
Environment.Exit()
```

應：

- 顯示明確提示
- 或 UI / Recorder 分離後只關閉 UI
- Recorder 狀態必須明確

如果使用者選擇停止錄影：

執行 Graceful Stop。

如果 UI crash：

Recorder 應依架構盡可能繼續。

---

# 75. 更新與版本策略

版本可使用：

```text
0.1.x Prototype

0.2.x Alpha

0.5.x Beta

1.0.x Stable
```

不得只因功能完成就升到 1.0。

1.0 Stable 必須符合 Stable Release Definition。

---

# 76. 開發優先順序

建議順序：

## Phase 1

- Architecture
- Core State Machine
- Session Persistence
- Logging
- Storage
- Windows basic capture
- MKV output

## Phase 2

- System audio
- Microphone
- Audio mixing / synchronization
- Remux

## Phase 3

- Recovery
- Process isolation
- Watchdog
- Disk monitoring

## Phase 4

- Region selection
- Multi-monitor
- DPI handling
- Device lost handling

## Phase 5

- Stress test
- Long-duration test
- Resource leak analysis
- Release hardening

## Phase 6

- Hardware encoder
- Global hotkey
- Tray
- UI polish

## Future

- macOS implementation

---

# 77. 第一個任務

現在不要直接開始把整個 ScreenRecorder 寫完。

第一步請：

1. 閱讀完整需求
2. 分析技術風險
3. 提出 Architecture
4. 提出 Solution / Project Structure
5. 列出 ADR
6. 將功能拆成 Milestones
7. 為每個 Milestone 定義 Acceptance Criteria
8. 建立 Testing Strategy
9. 特別說明 Recording Reliability / Recovery Strategy
10. 特別說明 Windows → macOS 的抽象界線
11. 檢查目前需求是否存在技術矛盾或高風險設計
12. 如果有更可靠的技術選擇，先提出理由，不要直接大改需求

然後建立：

```text
AGENTS.md

ARCHITECTURE.md

ROADMAP.md

TESTING.md

RELIABILITY.md

MANUAL_TEST_CHECKLIST.md

docs/adr/
```

先不要大量實作 Capture / Encoder。

等架構與第一階段計畫建立後，再從 Milestone 1 開始。

---

# 78. Codex 行為要求

你是此專案的主要開發代理人。

請遵守：

- 不要一次大量實作全部功能
- 不要為了快速完成而犧牲架構
- 不要為了架構漂亮而過度工程化
- 每次修改前先理解現有程式
- 每次修改後應 Build
- 有相關 Test 時應 Test
- 發現既有 Bug 應先分析根因
- 不要只修表面症狀
- 不要未經驗證就宣稱完成
- 不要偷偷忽略錯誤
- 不要任意移除既有可靠性機制
- 不要任意刪除測試
- 不要因測試難寫就取消驗收條件

若測試受限於硬體或 GUI 環境：

標示：

```text
REQUIRES MANUAL VALIDATION
```

並提供人工測試步驟。

---

# 79. 最重要的四條規則

永遠遵守：

> Reliability before features.

> Data safety before convenience.

> Never claim PASS without evidence.

> Never sacrifice recoverability for implementation simplicity.