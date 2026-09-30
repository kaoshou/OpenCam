# Windows 新擷取模式驗證（開發分支，尚未發布）

## 範圍與環境

- 分支：codex/windows-cursor-capture；基準 v0.2.4 / 13b1723。
- 保留 GDI 預設；增加 Desktop Duplication 測試選項。macOS 擷取與音訊元件未更換。
- 主機 macOS arm64，.NET 10 SDK 以 DOTNET_ROLL_FORWARD=Major 執行 net8.0 測試；精確 .NET 8／Windows 裝置驗收不由此替代。
- 未推送、未合併 master、未建立 Release；VERSION 不變。

## 已執行驗證

- 未修改基準：Core 80 + Media 274 通過，12 略過。
- 設定合約：Core 85 通過；未知設定值先得到失敗，再正規化為 GDI。
- 螢幕對應：13 通過／1 Windows-only 略過；覆蓋 3 台螢幕、負座標、實體像素、索引不等、鏡像歧義、跨 GPU／跨輸出／旋轉安全回退。
- FFmpeg 擷取能力、參數／四音源一致性與既有真實合成雙段 remux／解碼：16 通過。這不是 Windows 實際 DDA 擷取測試。
- 生命週期、首畫格、健康及輸出對應針對性測試：64 通過／1 略過。
- 設定、狀態與既有 UI 鎖定規則：69 通過。
- 審查修正後最終整體 .NET 回歸：Core 85 + Media 321 = **406 通過／0 失敗／13 略過**，含追加的 FFmpeg 錯誤分類及多 GPU 保護測試。審查前為 401 通過／13 略過。
- Release build：0 warnings / 0 errors。初始 sandbox 無法啟動測試 socket；授權環境重跑成功。NuGet audit 的暫時網路警告於 force restore 後消失，未關閉 audit。
- 網站測試 29 通過；網站 build 成功；macOS 原生音訊 helper tests 通過。
- 額外故障重現：日誌僅提及 ddagrab、真正錯誤為輸出權限不足時，不應改換擷取；GDI 備援的權限失敗亦不應再換 CPU。兩者均先觀察到錯誤分類，縮小分類條件後 RED → GREEN，並包含於上述完整回歸。

命令：

```sh
dotnet restore ScreenRecorder.sln --force
env DOTNET_ROLL_FORWARD=Major dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
env DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
npm ci --prefix website --cache /private/tmp/opencam-cursor-npm-cache
npm test --prefix website
npm run build --prefix website
bash tests/native/OpenCamSystemAudioTests.sh
git diff --check
```

本機執行紀錄：`/private/tmp/opencam-cursor-*.log`，不包含使用者實際錄影，也未上傳。

## 明確限制與裁決

- 只支援恰好一個硬體 D3D11 adapter、其輸出可唯一對應的環境；多硬體 GPU（包含 headless）、旋轉或不明拓樸回退。不是全 GPU 零拷貝實作，不保證 CPU 下降。
- 保存 OutputBounds 附加資料以檢查輸出位置變化；擷取選區本身不足以辨識拓樸變更。既有 session JSON 無此欄位仍可讀。
- 桌面可用性採只讀查詢，不呼叫 SwitchDesktop，不變更使用者桌面／解鎖狀態。
- 未知新版啟動失敗安全結束，不推測成編碼器失敗；代價是部分情況需手動選回 GDI。
- 現有 EncoderProbeRunner 負責有界輸出、逾時與清理，相關既有測試重跑；新增 probe 測試驗證 FFmpeg 回傳分類、必要能力、取消及清理失敗政策。
- IPC 分流、程式簽章與 Windows FFmpeg 對應原始碼核對不在本次改動中，既有限制不變。

## 獨立全分支審查與修正

獨立審查範圍 `13b1723..5b64e62`：未確認 Critical；兩項 Important 已接受，於同一修正階段處理，未以第二輪審查取代回歸測試。

修正前針對性回歸 **4 失敗／0 通過**；修正後針對性回歸 **51 通過／1 略過**；再補健康檢查案例並執行上述完整 **406 通過／13 略過**。文件同步後網站再次 **29 通過**、build 成功。

1. **跨程式 GPU 身分不能假設相同**：改用 IDXGIFactory1 列舉所有硬體 adapter，含未接螢幕者。僅恰好一個硬體 adapter 且 LUID 符合，才允許 DDA；多 GPU 及未知情況走 GDI。健康檢查也重查資格。兩個多 GPU 案例與空清單／不符 LUID 案例先失敗，再通過。新增硬體拓樸變更健康測試。
2. **實際 DDA 初始化錯誤漏判**：加入上游輸出不支援、duplication session 過多、DuplicateOutput／DXGI 初始化失敗訊息，仍須具 ddagrab filter 前綴。真實訊息測試先得到一般例外，修正後得到 CaptureStartupException。既有一次備援、獨立檔名、相同編碼器及輸出權限不得備援測試重跑。

參考：[Microsoft EnumAdapters1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1)、[DXGI_ADAPTER_DESC1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1)、[FFmpeg n8.0 ddagrab 原始碼](https://github.com/FFmpeg/FFmpeg/blob/n8.0/libavfilter/vsrc_ddagrab.c)。此上游資料核對不等於已驗證封裝 Windows FFmpeg 二進位的來源對應。

### 裁決及代價（依執行順序）

1. 原生 worktree 工具因桌面任務目錄非 repository 而失敗，改建相鄰 Git worktree；隔離 master，但桌面程式可能未登記附件。
2. 新增 nullable OutputBounds，才能辨識輸出移位；增加 session 資料欄位，舊 JSON 仍可讀。
3. 桌面健康檢查只讀名稱，不呼叫 SwitchDesktop；實際鎖定／解鎖行為待 Windows 驗收。
4. 未知新版初始化錯誤不推測成 encoder 錯誤；部分可恢復狀況須手動選 GDI。
5. 共用 partial 生命週期 fixture，沿用 EncoderProbeRunner 逾時／有界輸出測試；部分 capture 測試仍以既有 encoder 類別命名。
6. 主機以 .NET 10 roll-forward 測 net8.0；不能取代精確 .NET 8／Windows CI。
7. 限制單硬體 adapter，避免 FFmpeg 個別 GPU 偏好造成錄錯螢幕；混合／多 GPU 主機暫不能用 DDA。
8. IPC query/control 分流不納入此變更；既有 IPC 負載不因此改善。
9. 零拷貝、任意 GPU 路由、旋轉 DDA 暫緩；部分主機採 GDI，不承諾 CPU 降低。
10. 未發現新增失敗路徑，因此不重寫既有 shutdown wait／裝置列舉；既有等待或效能風險仍在。
11. COM／驅動、實體游標、Windows 長錄音畫同步、GUI 驗收標為 BLOCKED；不能宣稱已實機解決或直接據此發布。
12. Windows FFmpeg/source correspondence 與簽章不在此次驗證；既有來源稽核限制與信任警告仍在。

### 暫緩 Minor

- 預檢正常回退原因已顯示在 UI 並保存 session，但尚無專屬 structured log；遠端診斷仍需 UI／session 資料搭配 FFmpeg 指令日誌。

## BLOCKED：Windows 真實驗收

缺少受影響 Windows 實機。以下均不得標為 PASS：

1. 相同 1920×1080、30 FPS、Auto、預設游標，在 GDI／新版各錄 3 次，同時觀察實體游標及 MP4。
2. Intel／AMD 內顯、NVIDIA、3+ 螢幕、不同 DPI、負座標、旋轉、跨 GPU／跨螢幕回退及實體輸出身分。
3. 四音源各 5 分鐘、暫停切換音源／游標、30 分鐘音畫同步、原生錄影及波形。
4. 實際鎖定、螢幕拔除／模式改變、安全停止、FFmpeg／Recorder 強制中斷後救援與 remux 失敗。
5. 本機 GUI 的 Windows 中英文最長狀態／回退文字與設定頁視覺驗收。

成功標準：實體游標不再閃爍、影片範圍／游標／音訊正確且原有分段／救援功能不退步。只看 MP4 正常或 CI 綠燈，不能宣稱游標問題已修復。
