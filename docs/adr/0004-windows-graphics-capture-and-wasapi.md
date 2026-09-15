# ADR 0004: Windows 畫面擷取 (Windows.Graphics.Capture) 與 WASAPI 音訊架構

## 狀態 (Status)
已採納 (Accepted)

## 背景與問題脈絡 (Context)
Windows 平台上有多種畫面擷取途徑：
1. **GDI BitBlt**: 效能差、CPU 佔用極高、無法擷取硬體加速視窗、易產生黑畫面。
2. **DXGI Desktop Duplication (Desktop Duplication API)**:
   - 效能高，直接從 GPU 顯存擷取 DirectX Surface。
   - 缺點：在混合顯卡筆電、全螢幕獨佔視窗切換、解析度改變或自訂局部視窗時容易拋出 `DXGI_ERROR_ACCESS_LOST` 錯誤。
3. **Windows.Graphics.Capture (WGC)**:
   - Windows 10 1809+ / Windows 11 引入的現代化 GPU 擷取 API。
   - 支援全螢幕、指定 Monitor、指定視窗或矩形剪裁。
   - 支援無黃色邊框擷取 (IsBorderRequired = false，Windows 10 2004+)。
   - 不易因視窗最小化或顯示卡切換而直接當機。

音訊部分：
- Windows 的現代標準為 **WASAPI (Windows Audio Session API)**。
- 系統音訊需要使用 WASAPI Loopback 模式。
- 麥克風需要使用 WASAPI Capture 模式。

## 決策 (Decision)
1. 畫面擷取：
   - 優先採用 **Windows.Graphics.Capture** (搭配 Direct3D 11)。
   - 所有 Windows-specific 呼叫封裝在 `ScreenRecorder.Platform.Windows` 模組中，透過實作跨平台介面 `IVideoCaptureService` 暴露給核心層，嚴禁在 `ScreenRecorder.Core` 中引用 Windows 命名空間。
2. 音訊擷取：
   - 採用 **WASAPI** (透過 NAudio 或原生 COM P/Invoke) 進行雙軌收集：
     - 軌道一：系統聲音 (WASAPI Loopback)。
     - 軌道二：麥克風聲音 (WASAPI AudioClient)。
   - 音訊異常處理：
     - 當麥克風插拔或預設音訊設備更換時，捕捉設備失效事件，發布日誌與告警，畫面錄製持續進行，不得因單一音訊裝置離線而使整體錄影中斷。

## 替代方案評估 (Alternatives)
- 未來 macOS 移植：
  - macOS 上的 `ScreenCaptureKit` 將以相同方式實作 `IVideoCaptureService` 與 `IAudioCaptureService`，替換掉 Windows 實作即可，核心流程完全不受影響。

## 後續影響與風險 (Consequences & Risks)
- Windows.Graphics.Capture 要求 Windows 10 版本 1809 以上，符合目前主流支援規格 (Windows 10/11)。
