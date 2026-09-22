// SPDX-License-Identifier: AGPL-3.0-or-later
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';

export const content = {
  'zh-TW': {
    eyebrow: 'Windows 與 macOS 的簡單螢幕錄影工具',
    heroTitle: '把重要的畫面，<br><em>好好錄下來。</em>',
    heroText: '選擇整個螢幕或指定範圍，一鍵開始錄影。OpenCam 先保存 MKV 工作檔，完成後再封裝成 MP4。',
    downloadLabel: '下載 OpenCam', guideLabel: '閱讀使用說明',
    openSource: '開放原始碼 · 免費使用',
    licenseReleaseNote: '目前原始碼為 0.2.0（AGPL-3.0-or-later）；下載連結仍指向 v0.1.5，該舊版保留原授權。',
    screenshotTitle: '每一步，都清楚直覺。',
    screenshotText: '從錄影範圍、聲音來源到儲存位置，常用選項都在眼前。',
    mainAlt: 'OpenCam 繁體中文主畫面，顯示錄影來源、編碼器與音訊選項',
    settingsAlt: 'OpenCam 繁體中文設定視窗，顯示一般設定與儲存選項',
    mainCaption: '主畫面 · Windows 介面預覽', settingsCaption: '偏好設定 · Windows 介面預覽',
    featuresTitle: '簡單上手，細節也到位。',
    features: [
      ['01', '指定螢幕或自訂區域', '完整錄製指定顯示器，或以透明選取框框住真正需要的範圍。'],
      ['02', '聲音自由選擇', '系統聲音與麥克風分別開關，錄下你想保留的聲音。'],
      ['03', '游標效果', '原始游標、黃色光暈、點擊漣漪與隱藏游標，依內容選擇。'],
      ['04', '暫停後可調整', '暫停期間可變更音訊開關與游標樣式，再繼續錄製。'],
      ['05', '自動選擇編碼器', '優先使用可用的硬體編碼；必要時自動回退 CPU。'],
      ['06', '修復救援', '若意外中斷，可嘗試救回已寫入的 MKV 錄影片段。']
    ],
    processEyebrow: '錄影機制', processTitle: '先保留錄影，再交付 MP4。',
    processText: '錄影時先寫入 MKV 工作檔。正常停止後，OpenCam 會驗證並無損封裝為 MP4；意外中斷時，可從尚存的工作檔嘗試修復。',
    processSteps: ['開始錄影', '寫入 MKV', '停止並驗證', '封裝 MP4'],
    platformsTitle: '選擇你的平台', windowsTitle: 'Windows', windowsText: 'Windows 10／11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 以上 · Apple Silicon（arm64）',
    guideTitle: '第一次使用？從這裡開始。', guideText: '完整說明錄影模式、各項參數、檔案位置與修復救援。',
    previewNote: '畫面為既有 Windows 版本的真實截圖，可能與最新版本略有差異。'
  },
  'en-US': {
    eyebrow: 'Simple screen recording for Windows and macOS',
    heroTitle: 'Capture what matters.<br><em>Keep it safe.</em>',
    heroText: 'Choose a display or a precise region, then start recording. OpenCam saves an MKV working file first and packages it as MP4 when you finish.',
    downloadLabel: 'Download OpenCam', guideLabel: 'Read the user guide',
    openSource: 'Open source · Free to use',
    licenseReleaseNote: 'Current source: 0.2.0 (AGPL-3.0-or-later). The download still points to v0.1.5, which retains its original license.',
    screenshotTitle: 'Clear from the first click.',
    screenshotText: 'Capture area, audio sources, and output location are right where you need them.',
    mainAlt: 'OpenCam English main window showing capture, encoder, and audio controls',
    settingsAlt: 'OpenCam English preferences window showing general and storage settings',
    mainCaption: 'Main window · Windows interface preview', settingsCaption: 'Preferences · Windows interface preview',
    featuresTitle: 'Easy to start. Ready for the details.',
    features: [
      ['01', 'Display or custom region', 'Capture a selected monitor or frame exactly the area you need with a transparent selection.'],
      ['02', 'Choose your audio', 'Switch system audio and microphone recording independently.'],
      ['03', 'Cursor effects', 'Use the native pointer, yellow halo, click ripple, or hidden cursor.'],
      ['04', 'Adjust while paused', 'Change audio switches and cursor style during a pause, then continue recording.'],
      ['05', 'Automatic encoder choice', 'Prefer available hardware encoding and fall back to CPU when needed.'],
      ['06', 'Crash Recovery', 'After an interruption, attempt to salvage MKV segments already written.']
    ],
    processEyebrow: 'How recording works', processTitle: 'Preserve first. Deliver MP4 afterward.',
    processText: 'OpenCam writes an MKV working file while recording. After a normal stop, it validates and losslessly remuxes the result to MP4. If interrupted, the working file may be recoverable.',
    processSteps: ['Start capture', 'Write MKV', 'Stop & validate', 'Package MP4'],
    platformsTitle: 'Choose your platform', windowsTitle: 'Windows', windowsText: 'Windows 10/11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 or later · Apple Silicon (arm64)',
    guideTitle: 'New to OpenCam? Start here.', guideText: 'A complete guide to capture modes, settings, file locations, and Crash Recovery.',
    previewNote: 'These genuine Windows screenshots may differ slightly from the latest version.'
  }
};

export function renderHome(language) {
  const c = content[language];
  if (!c) throw new TypeError('Unsupported site language');
  const suffix = language === 'zh-TW' ? 'zhtw' : 'enus';
  const guide = routeFor(language, 'guide');
  const latest = 'https://github.com/kaoshou/OpenCam/releases/latest';
  const featureCards = c.features.map(([number, title, text]) => `<li class="feature-card"><span class="feature-number">${number}</span><h3>${escapeHtml(title)}</h3><p>${escapeHtml(text)}</p></li>`).join('');
  return `<section class="hero"><div class="container hero-grid"><div class="hero-copy"><p class="eyebrow"><span class="eyebrow-dot"></span>${escapeHtml(c.eyebrow)}</p><h1>${c.heroTitle}</h1><p class="hero-text">${escapeHtml(c.heroText)}</p><div class="hero-actions"><a class="button button-primary" href="${latest}">${escapeHtml(c.downloadLabel)} <span aria-hidden="true">↗</span></a><a class="button button-secondary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div><p class="hero-meta">${escapeHtml(c.openSource)}</p><p class="release-license-note">${escapeHtml(c.licenseReleaseNote)}</p></div><div class="hero-visual"><div class="window-halo"></div><img src="${assetPath(`preview_main_${suffix}.png`)}" alt="${escapeHtml(c.mainAlt)}" width="1100" height="730"><span class="visual-caption">${escapeHtml(c.mainCaption)}</span></div></div></section>
<section class="section screenshot-section" id="preview"><div class="container"><div class="section-intro"><p class="section-kicker">01 / OpenCam</p><h2>${escapeHtml(c.screenshotTitle)}</h2><p>${escapeHtml(c.screenshotText)}</p></div><div class="screenshot-grid"><figure class="screenshot-card"><img src="${assetPath(`preview_main_${suffix}.png`)}" alt="${escapeHtml(c.mainAlt)}" loading="lazy"><figcaption>${escapeHtml(c.mainCaption)}</figcaption></figure><figure class="screenshot-card"><img src="${assetPath(`preview_settings_${suffix}.png`)}" alt="${escapeHtml(c.settingsAlt)}" loading="lazy"><figcaption>${escapeHtml(c.settingsCaption)}</figcaption></figure></div><p class="preview-note">${escapeHtml(c.previewNote)}</p></div></section>
<section class="section features-section" id="features"><div class="container"><div class="section-intro"><p class="section-kicker">02 / Features</p><h2>${escapeHtml(c.featuresTitle)}</h2></div><ul class="feature-grid">${featureCards}</ul></div></section>
<section class="section process-section"><div class="container process-grid"><div><p class="section-kicker">03 / ${escapeHtml(c.processEyebrow)}</p><h2>${escapeHtml(c.processTitle)}</h2><p class="process-text">${escapeHtml(c.processText)}</p><a class="text-link" href="${guide}">${escapeHtml(c.guideLabel)} →</a></div><ol class="process-steps">${c.processSteps.map((step, index) => `<li><span>0${index + 1}</span><strong>${escapeHtml(step)}</strong></li>`).join('')}</ol></div></section>
<section class="section platform-section"><div class="container"><div class="section-intro"><p class="section-kicker">04 / Platforms</p><h2>${escapeHtml(c.platformsTitle)}</h2></div><div class="platform-grid"><div class="platform-card"><span class="platform-symbol" aria-hidden="true">▦</span><h3>${escapeHtml(c.windowsTitle)}</h3><p>${escapeHtml(c.windowsText)}</p></div><div class="platform-card"><span class="platform-symbol" aria-hidden="true">⌘</span><h3>${escapeHtml(c.macTitle)}</h3><p>${escapeHtml(c.macText)}</p></div></div></div></section>
<section class="section final-cta"><div class="container final-cta-inner"><div><p class="section-kicker">05 / Guide</p><h2>${escapeHtml(c.guideTitle)}</h2><p>${escapeHtml(c.guideText)}</p></div><a class="button button-primary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div></section>`;
}
