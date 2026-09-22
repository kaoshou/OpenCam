// SPDX-License-Identifier: AGPL-3.0-or-later
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';

export const content = {
  'zh-TW': {
    eyebrow: 'Windows 與 macOS 的簡單螢幕錄影工具',
    heroTitle: '<span class="hero-line">把重要的畫面，</span><em class="hero-line">好好錄下來。</em>',
    heroText: '選擇整個螢幕或指定範圍，一鍵開始錄影。OpenCam 先保存 MKV 工作檔，完成後再封裝成 MP4。',
    downloadLabel: '下載 OpenCam', guideLabel: '閱讀使用說明',
    openSource: '開放原始碼 · 免費使用',
    licenseReleaseNote: '下載 v0.2.1（AGPL-3.0-or-later）；Windows 與 Apple Silicon Mac 安裝包由 GitHub Actions 建置。',
    screenshotTitle: '每一步，都清楚直覺。',
    screenshotText: '最新版 macOS 介面：錄影範圍、音訊來源與狀態監控一目了然。點開圖片可檢視原尺寸文字。',
    mainAlt: 'OpenCam 0.2.0 macOS 繁體中文主畫面，包含多螢幕辨識按鈕、錄影設定與狀態監控區',
    settingsAlt: 'OpenCam 0.2.0 macOS 繁體中文偏好設定視窗',
    mainCaption: '主畫面 · macOS 0.2.0', settingsCaption: '偏好設定 · macOS 0.2.0',
    fullSizeLabel: '開啟原尺寸圖片',
    featuresTitle: '簡單上手，細節也到位。',
    features: [
      ['01', '指定螢幕或自訂區域', '辨識每台顯示器，或用透明、可調整的選取框圈出錄影範圍。'],
      ['02', '分開監看兩種聲音', '系統聲音與麥克風可獨立開關；錄影中分別顯示即時音量波形。'],
      ['03', '游標效果', '原始游標、黃色光暈、點擊漣漪與隱藏游標，依內容選擇。'],
      ['04', '暫停後可調整', '暫停期間可變更音訊開關與游標樣式，再繼續錄製。'],
      ['05', '防誤關與自動回退', '錄影中關閉視窗會先提醒；硬體編碼不可用時可回退 CPU。'],
      ['06', '修復救援', '意外中斷後，可嘗試救回已寫入的 MKV 錄影片段。']
    ],
    processEyebrow: '錄影機制', processTitle: '先保留錄影，再交付 MP4。',
    processText: '錄影時先寫入 MKV 工作檔。正常停止後，OpenCam 會驗證並無損封裝為 MP4；意外中斷時，可從尚存的工作檔嘗試修復。',
    processSteps: ['開始錄影', '寫入 MKV', '停止並驗證', '封裝 MP4'],
    platformsTitle: '選擇你的平台', windowsTitle: 'Windows', windowsText: 'Windows 10／11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 以上 · Apple Silicon（arm64）',
    guideTitle: '第一次使用？從這裡開始。', guideText: '完整說明錄影模式、各項參數、檔案位置與修復救援。',
    previewNote: '畫面由 v0.2.0 macOS 應用程式擷取；Windows 介面與裝置選項可能不同。'
  },
  'en-US': {
    eyebrow: 'Simple screen recording for Windows and macOS',
    heroTitle: '<span class="hero-line">Capture what matters.</span><em class="hero-line">Keep it safe.</em>',
    heroText: 'Choose a display or a precise region, then start recording. OpenCam saves an MKV working file first and packages it as MP4 when you finish.',
    downloadLabel: 'Download OpenCam', guideLabel: 'Read the user guide',
    openSource: 'Open source · Free to use',
    licenseReleaseNote: 'Download v0.2.1 (AGPL-3.0-or-later). Windows and Apple Silicon Mac packages are built by GitHub Actions.',
    screenshotTitle: 'Clear from the first click.',
    screenshotText: 'The current macOS interface puts capture, audio, and recording status in view. Open an image at full size to read its labels.',
    mainAlt: 'OpenCam 0.2.0 macOS English main window with display identification, capture settings, and recording status',
    settingsAlt: 'OpenCam 0.2.0 macOS English preferences window',
    mainCaption: 'Main window · macOS 0.2.0', settingsCaption: 'Preferences · macOS 0.2.0',
    fullSizeLabel: 'Open full-size image',
    featuresTitle: 'Easy to start. Ready for the details.',
    features: [
      ['01', 'Display or custom region', 'Identify every connected display, or frame a region with the transparent, resizable selector.'],
      ['02', 'Monitor audio separately', 'Switch system audio and microphone independently, and see separate live level waveforms.'],
      ['03', 'Cursor effects', 'Use the native pointer, yellow halo, click ripple, or hidden cursor.'],
      ['04', 'Adjust while paused', 'Change audio switches and cursor style during a pause, then continue recording.'],
      ['05', 'Close protection and fallback', 'Closing during recording prompts a warning; unavailable hardware encoding can fall back to CPU.'],
      ['06', 'Crash Recovery', 'After an interruption, attempt to salvage MKV segments already written.']
    ],
    processEyebrow: 'How recording works', processTitle: 'Preserve first. Deliver MP4 afterward.',
    processText: 'OpenCam writes an MKV working file while recording. After a normal stop, it validates and losslessly remuxes the result to MP4. If interrupted, the working file may be recoverable.',
    processSteps: ['Start capture', 'Write MKV', 'Stop & validate', 'Package MP4'],
    platformsTitle: 'Choose your platform', windowsTitle: 'Windows', windowsText: 'Windows 10/11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 or later · Apple Silicon (arm64)',
    guideTitle: 'New to OpenCam? Start here.', guideText: 'A complete guide to capture modes, settings, file locations, and Crash Recovery.',
    previewNote: 'Captured from the v0.2.0 macOS app; the Windows interface and device options may differ.'
  }
};

export function renderHome(language) {
  const c = content[language];
  if (!c) throw new TypeError('Unsupported site language');
  const suffix = language === 'zh-TW' ? 'zhtw' : 'enus';
  const guide = routeFor(language, 'guide');
  const latest = 'https://github.com/kaoshou/OpenCam/releases';
  const featureCards = c.features.map(([number, title, text]) => `<li class="feature-card"><span class="feature-number">${number}</span><h3>${escapeHtml(title)}</h3><p>${escapeHtml(text)}</p></li>`).join('');
  const mainImage = assetPath(`preview_main_${suffix}.png`);
  const settingsImage = assetPath(`preview_settings_${suffix}.png`);
  return `<section class="hero"><div class="container hero-grid"><div class="hero-copy"><p class="eyebrow"><span class="eyebrow-dot"></span>${escapeHtml(c.eyebrow)}</p><h1>${c.heroTitle}</h1><p class="hero-text">${escapeHtml(c.heroText)}</p><div class="hero-actions"><a class="button button-primary" href="${latest}">${escapeHtml(c.downloadLabel)} <span aria-hidden="true">↗</span></a><a class="button button-secondary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div><p class="hero-meta">${escapeHtml(c.openSource)}</p><p class="release-license-note">${escapeHtml(c.licenseReleaseNote)}</p></div><div class="hero-visual"><div class="window-halo"></div><a class="screenshot-link" href="${mainImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" width="840" height="720"></a><span class="visual-caption">${escapeHtml(c.mainCaption)} · ${escapeHtml(c.fullSizeLabel)}</span></div></div></section>
<section class="section screenshot-section" id="preview"><div class="container"><div class="section-intro"><p class="section-kicker">01 / OpenCam</p><h2>${escapeHtml(c.screenshotTitle)}</h2><p>${escapeHtml(c.screenshotText)}</p></div><div class="screenshot-grid"><figure class="screenshot-card"><a class="screenshot-link" href="${mainImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" loading="lazy" width="840" height="720"></a><figcaption>${escapeHtml(c.mainCaption)} · <a href="${mainImage}" target="_blank" rel="noopener noreferrer">${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><figure class="screenshot-card settings-card"><a class="screenshot-link" href="${settingsImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${settingsImage}" alt="${escapeHtml(c.settingsAlt)}" loading="lazy" width="650" height="820"></a><figcaption>${escapeHtml(c.settingsCaption)} · <a href="${settingsImage}" target="_blank" rel="noopener noreferrer">${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div><p class="preview-note">${escapeHtml(c.previewNote)}</p></div></section>
<section class="section features-section" id="features"><div class="container"><div class="section-intro"><p class="section-kicker">02 / Features</p><h2>${escapeHtml(c.featuresTitle)}</h2></div><ul class="feature-grid">${featureCards}</ul></div></section>
<section class="section process-section"><div class="container process-grid"><div><p class="section-kicker">03 / ${escapeHtml(c.processEyebrow)}</p><h2>${escapeHtml(c.processTitle)}</h2><p class="process-text">${escapeHtml(c.processText)}</p><a class="text-link" href="${guide}">${escapeHtml(c.guideLabel)} →</a></div><ol class="process-steps">${c.processSteps.map((step, index) => `<li><span>0${index + 1}</span><strong>${escapeHtml(step)}</strong></li>`).join('')}</ol></div></section>
<section class="section platform-section"><div class="container"><div class="section-intro"><p class="section-kicker">04 / Platforms</p><h2>${escapeHtml(c.platformsTitle)}</h2></div><div class="platform-grid"><div class="platform-card"><span class="platform-symbol" aria-hidden="true">▦</span><h3>${escapeHtml(c.windowsTitle)}</h3><p>${escapeHtml(c.windowsText)}</p></div><div class="platform-card"><span class="platform-symbol" aria-hidden="true">⌘</span><h3>${escapeHtml(c.macTitle)}</h3><p>${escapeHtml(c.macText)}</p></div></div></div></section>
<section class="section final-cta"><div class="container final-cta-inner"><div><p class="section-kicker">05 / Guide</p><h2>${escapeHtml(c.guideTitle)}</h2><p>${escapeHtml(c.guideText)}</p></div><a class="button button-primary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div></section>`;
}
