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
- 最終整體 .NET 回歸：Core 85 + Media 316 = **401 通過／0 失敗／13 略過**，含追加的 FFmpeg 錯誤分類測試。
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

- 只支援可唯一對應的預設 D3D11 adapter 輸出；其他 adapter、旋轉或不明拓樸回退。不是全 GPU 零拷貝實作，不保證 CPU 下降。
- 保存 OutputBounds 附加資料以檢查輸出位置變化；擷取選區本身不足以辨識拓樸變更。既有 session JSON 無此欄位仍可讀。
- 桌面可用性採只讀查詢，不呼叫 SwitchDesktop，不變更使用者桌面／解鎖狀態。
- 未知新版啟動失敗安全結束，不推測成編碼器失敗；代價是部分情況需手動選回 GDI。
- 現有 EncoderProbeRunner 負責有界輸出、逾時與清理，相關既有測試重跑；新增 probe 測試驗證 FFmpeg 回傳分類、必要能力、取消及清理失敗政策。
- IPC 分流、程式簽章與 Windows FFmpeg 對應原始碼核對不在本次改動中，既有限制不變。

## BLOCKED：Windows 真實驗收

缺少受影響 Windows 實機。以下均不得標為 PASS：

1. 相同 1920×1080、30 FPS、Auto、預設游標，在 GDI／新版各錄 3 次，同時觀察實體游標及 MP4。
2. Intel／AMD 內顯、NVIDIA、3+ 螢幕、不同 DPI、負座標、旋轉、跨 GPU／跨螢幕回退及實體輸出身分。
3. 四音源各 5 分鐘、暫停切換音源／游標、30 分鐘音畫同步、原生錄影及波形。
4. 實際鎖定、螢幕拔除／模式改變、安全停止、FFmpeg／Recorder 強制中斷後救援與 remux 失敗。
5. 本機 GUI 的 Windows 中英文最長狀態／回退文字與設定頁視覺驗收。

成功標準：實體游標不再閃爍、影片範圍／游標／音訊正確且原有分段／救援功能不退步。只看 MP4 正常或 CI 綠燈，不能宣稱游標問題已修復。
