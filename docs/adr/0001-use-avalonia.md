# ADR 0001: 選擇 Avalonia UI 作為跨平台使用者介面框架

## 狀態 (Status)
已採納 (Accepted)

## 背景與問題脈絡 (Context)
本專案目標是打造一個高可靠度、操作直覺（類似 oCam）的螢幕錄影工具。
第一階段雖然以 Windows 10/11 x64 為主要運作環境，但專案架構從首日開始即明確要求具備未來移植至 macOS 的能力，禁止將系統做成 Windows-only 架構。
在 .NET 生態中，常見的桌面 UI 框架包括：
1. **WPF**: 成熟穩定，但僅限於 Windows 平台，無法移植至 macOS。
2. **WinUI 3 / Windows App SDK**: 專注於 Windows 10/11，無法跨平台至 macOS，且打包與 runtime 依賴複雜。
3. **MAUI**: 雖支援跨平台，但桌面端支援度和透明 Overlay / 視窗層級控制較弱，多螢幕座標處理較多限制。
4. **Avalonia UI**: 跨平台（支援 Windows、macOS、Linux），架構與 WPF / XAML 相似，擁有健全的社群支援、優秀的 Render pipeline 與透明無邊框視窗支援（適合錄影選區 Overlay）。

## 決策 (Decision)
決定採用 **Avalonia UI 11.x** 搭配 **MVVM** 模式作為前端介面框架：
1. UI 專案為獨立前端 (`ScreenRecorder.UI`)，負責參數設定、錄影區域 Overlay 選取、進度顯示與 Recovery 對話框。
2. UI 嚴格遵守「不直接執行錄影與擷取底層邏輯」，所有操作均透過 IPC 與 Recorder 行程溝通。
3. 視窗控制與選取框採用 Avalonia 跨平台視窗抽象，並在 Windows 下配合 Platform 服務換算精確的虛擬螢幕座標與 DPI Scaling。

## 替代方案評估 (Alternatives)
- **WPF**: 若採用 WPF，雖然開發 Windows 介面最快，但日後移植 macOS 時必須整套 UI 打掉重寫，違反 AGENTS.md 跨平台架構設計初衷。
- **Webview / Tauri / Electron**: 記憶體佔用較大，對於螢幕選取框的穿透與低延遲 Overlay 渲染較為繁複，且引入另一種語言棧（JS/Rust）。

## 後續影響與風險 (Consequences & Risks)
- **優點**: 
  - 核心 ViewModel 與 UI 邏輯可在 Windows 與 macOS 之間最大程度共用。
  - 支援透明無邊框視窗，容易實作如 oCam 風格的螢幕選取矩形框。
- **風險與緩解措施**:
  - Avalonia 在不同作業系統的視窗與螢幕座標 API 可能有細微差異。
  - 緩解：將螢幕識別、工作區與 DPI 換算邏輯抽象化為 `IDisplayService`，Windows 版本放在 `ScreenRecorder.Platform.Windows` 內由 Win32 / DXGI 原生 API 提供精確座標。
