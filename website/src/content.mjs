// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';

const versionText = readFileSync(new URL('../../VERSION', import.meta.url), 'utf8');
if (!/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\n$/.test(versionText)) {
  throw new Error('VERSION must contain numeric SemVer and exactly one final LF');
}
export const productVersion = versionText.slice(0, -1);
const displayVersion = `v${productVersion}`;
export const mainScreenshotVersion = JSON.parse(readFileSync(new URL('../../docs/images/metadata.json', import.meta.url), 'utf8')).version;
const mainScreenshotDisplayVersion = `v${mainScreenshotVersion}`;

export const content = {
  'zh-TW': {
    eyebrow: 'Windows 與 macOS 的簡單錄影與剪輯工具',
    heroTitle: '<span class="hero-line">把重要的畫面，</span><em class="hero-line">好好錄下來。</em>',
    heroText: '錄影、剪輯、續錄，一次完成。選擇螢幕或範圍即可錄製；需要整理時，再裁剪、重排片段並輸出 MP4。原始 MKV 素材保留在專案中。',
    downloadLabel: '下載 OpenCam', guideLabel: '閱讀使用說明',
    openSource: '開放原始碼 · 免費使用',
    licenseReleaseNote: `此頁介紹準備中的 ${displayVersion}，尚未發布；Windows 實機驗收與 FFmpeg 來源核對仍待完成。已發布版本請至 Releases 下載。AGPL-3.0-or-later。`,
    screenshotTitle: '每一步，都清楚直覺。',
    screenshotText: '從開始錄影到整理片段，保留熟悉而簡單的操作。錄影中分別查看兩種收音波形；需要剪輯時，再開啟時間軸。點開圖片可閱讀原尺寸文字。',
    mainAlt: `OpenCam ${mainScreenshotVersion} macOS 繁體中文錄影中主畫面，右側顯示系統聲音與麥克風收音波形`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS 繁體中文偏好設定視窗`,
    mainCaption: `錄影中與收音波形 · macOS ${mainScreenshotVersion}`, settingsCaption: `偏好設定 · macOS ${mainScreenshotVersion}`,
    editorTitle: '錄好的內容，輕鬆剪輯。',
    editorText: '上方預覽、下方時間軸，搭配縮圖與波形定位。分割、裁剪或刪除不需要的部分，也能拖曳左側片段清單調整順序。儲存專案不必立即輸出 MP4，下次可從首頁開啟並追加錄製。',
    editorAlt: `OpenCam ${mainScreenshotVersion} 繁體中文剪輯器，顯示教學簡報影片、片段縮圖、音訊波形與時間軸`,
    editorCaption: `編輯錄製內容 · ${mainScreenshotVersion} · 自製教學影片`,
    editorPoints: ['原始素材保留，支援復原與重做', '預覽畫質、大小、全螢幕與聲音開關', '儲存專案與輸出 MP4 分開操作'],
    fullSizeLabel: '開啟原尺寸圖片',
    featuresTitle: '簡單上手，細節也到位。',
    features: [
      ['01', '指定螢幕或自訂區域', '辨識每台顯示器，或用透明、可調整的選取框圈出錄影範圍。'],
      ['02', '分開監看兩種聲音', '系統聲音與麥克風可獨立開關；錄影中分別顯示即時音量波形。'],
      ['03', '時間軸剪輯', '用縮圖與波形定位，分割、裁剪、刪除範圍並重排片段。'],
      ['04', '保存專案，下次續錄', '可只儲存專案、不輸出 MP4；日後重新開啟，再追加錄影與編輯。'],
      ['05', '防誤關與自動回退', '錄影中關閉視窗會先提醒；硬體編碼不可用時可回退 CPU。'],
      ['06', '修復救援', '意外中斷後，可嘗試救回已寫入的 MKV 錄影片段。']
    ],
    processEyebrow: '錄影機制', processTitle: '先保留錄影，再交付 MP4。',
    processText: '錄影先寫入 MKV，專案保留原始素材與剪輯決策。停止後可自動輸出，也可留到編輯完成再輸出。相容的完整素材可快速封裝；裁剪、效果或不相容素材會重新編碼，輸出時間依內容而定。',
    processSteps: ['錄製並保留 MKV', '儲存或繼續編輯', '下次開啟並續錄', '驗證並輸出 MP4'],
    platformsTitle: '選擇你的平台', windowsTitle: 'Windows', windowsText: 'Windows 10／11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 以上 · Apple Silicon（arm64）',
    guideTitle: '第一次使用？從這裡開始。', guideText: '完整說明錄影、剪輯、儲存與續錄、MP4 輸出和修復救援。',
    previewNote: `圖片由 ${mainScreenshotDisplayVersion} 程式介面離線渲染；錄影狀態為示範資料，剪輯器使用自製影片。這些圖片不是實機收音或跨平台驗收證據。Windows 介面與裝置選項可能不同。`
  },
  'en-US': {
    eyebrow: 'Simple recording and editing for Windows and macOS',
    heroTitle: '<span class="hero-line">Capture what matters.</span><em class="hero-line">Keep it safe.</em>',
    heroText: 'Record, edit and continue in one place. Capture a display or region, then trim and reorder clips when needed. Export MP4 while keeping the original MKV media in your project.',
    downloadLabel: 'Download OpenCam', guideLabel: 'Read the user guide',
    openSource: 'Open source · Free to use',
    licenseReleaseNote: `This page previews ${displayVersion}, not yet released. Windows device acceptance and FFmpeg source verification remain pending. Download published versions from Releases. AGPL-3.0-or-later.`,
    screenshotTitle: 'Clear from the first click.',
    screenshotText: 'Keep recording simple, then open the timeline when you need to edit. Separate audio waveforms help monitor capture. Open each image at full size to read its labels.',
    mainAlt: `OpenCam ${mainScreenshotVersion} macOS English Recording screen with live system-audio and microphone audio waveforms`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS English preferences window`,
    mainCaption: `Recording with audio waveforms · macOS ${mainScreenshotVersion}`, settingsCaption: `Preferences · macOS ${mainScreenshotVersion}`,
    editorTitle: 'Turn your recording into a clearer story.',
    editorText: 'Preview above, timeline below. Use thumbnails and waveforms to find the right moment, split and trim clips, remove a range, or drag the clip list to reorder. Save the project without exporting, then reopen it from home to append another recording.',
    editorAlt: `OpenCam ${mainScreenshotVersion} English editor with an authored tutorial video, clip thumbnails, audio waveforms and timeline`,
    editorCaption: `Edit recording · ${mainScreenshotVersion} · Authored tutorial video`,
    editorPoints: ['Keep source media, with undo and redo', 'Choose preview quality, size, full screen and sound on/off', 'Save projects independently of MP4 export'],
    fullSizeLabel: 'Open full-size image',
    featuresTitle: 'Easy to start. Ready for the details.',
    features: [
      ['01', 'Display or custom region', 'Identify every connected display, or frame a region with the transparent, resizable selector.'],
      ['02', 'Monitor audio separately', 'Switch system audio and microphone independently, and see separate live level waveforms.'],
      ['03', 'Timeline editing', 'Locate content with thumbnails and waveforms, then split, trim, delete ranges and reorder clips.'],
      ['04', 'Save now, record again later', 'Save a project without exporting MP4, reopen it later and append more recordings.'],
      ['05', 'Close protection and fallback', 'Closing during recording prompts a warning; unavailable hardware encoding can fall back to CPU.'],
      ['06', 'Crash Recovery', 'After an interruption, attempt to salvage MKV segments already written.']
    ],
    processEyebrow: 'How recording works', processTitle: 'Preserve first. Deliver MP4 afterward.',
    processText: 'Recordings are written to MKV; projects retain source media and editing decisions. Export automatically on stop or wait until editing is finished. Compatible whole recordings can be remuxed quickly; trims, effects or incompatible media require encoding. Export time depends on the content.',
    processSteps: ['Record and retain MKV', 'Save or edit', 'Reopen and append', 'Verify and export MP4'],
    platformsTitle: 'Choose your platform', windowsTitle: 'Windows', windowsText: 'Windows 10/11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 or later · Apple Silicon (arm64)',
    guideTitle: 'New to OpenCam? Start here.', guideText: 'A complete guide to recording, editing, saving, resuming, MP4 export and Crash Recovery.',
    previewNote: `Images are offline renders of the ${mainScreenshotDisplayVersion} application UI. Recording status is illustrative; the editor uses an authored demo video. They are not evidence of hardware audio capture or cross-platform acceptance. Windows controls and devices may differ.`
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
  const editorImage = assetPath(`preview_editor_${suffix}.png`);
  return `<section class="hero"><div class="container hero-grid"><div class="hero-copy"><p class="eyebrow"><span class="eyebrow-dot"></span>${escapeHtml(c.eyebrow)}</p><h1>${c.heroTitle}</h1><p class="hero-text">${escapeHtml(c.heroText)}</p><div class="hero-actions"><a class="button button-primary" href="${latest}">${escapeHtml(c.downloadLabel)} <span aria-hidden="true">↗</span></a><a class="button button-secondary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div><p class="hero-meta">${escapeHtml(c.openSource)}</p><p class="release-license-note">${escapeHtml(c.licenseReleaseNote)}</p></div><div class="hero-visual"><div class="window-halo"></div><a class="screenshot-link" href="${mainImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" width="840" height="780"></a><span class="visual-caption">${escapeHtml(c.mainCaption)} · ${escapeHtml(c.fullSizeLabel)}</span></div></div></section>
<section class="section screenshot-section" id="preview"><div class="container"><div class="section-intro"><p class="section-kicker">01 / OpenCam</p><h2>${escapeHtml(c.screenshotTitle)}</h2><p>${escapeHtml(c.screenshotText)}</p></div><div class="screenshot-grid"><figure class="screenshot-card"><a class="screenshot-link" href="${mainImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" loading="lazy" width="840" height="780"></a><figcaption>${escapeHtml(c.mainCaption)} · <a href="${mainImage}" target="_blank" rel="noopener noreferrer">${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><figure class="screenshot-card settings-card"><a class="screenshot-link" href="${settingsImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${settingsImage}" alt="${escapeHtml(c.settingsAlt)}" loading="lazy" width="650" height="820"></a><figcaption>${escapeHtml(c.settingsCaption)} · <a href="${settingsImage}" target="_blank" rel="noopener noreferrer">${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div><p class="preview-note">${escapeHtml(c.previewNote)}</p></div></section>
<section class="section editor-section" id="editing"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(mainScreenshotVersion)}</p><h2>${escapeHtml(c.editorTitle)}</h2><p>${escapeHtml(c.editorText)}</p></div><figure class="screenshot-card editor-card"><a class="screenshot-link" href="${editorImage}" target="_blank" rel="noopener noreferrer" aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${editorImage}" alt="${escapeHtml(c.editorAlt)}" loading="lazy" width="1440" height="900"></a><figcaption>${escapeHtml(c.editorCaption)} · <a href="${editorImage}" target="_blank" rel="noopener noreferrer">${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><ul class="editor-highlights">${c.editorPoints.map(point => `<li>${escapeHtml(point)}</li>`).join('')}</ul></div></section>
<section class="section features-section" id="features"><div class="container"><div class="section-intro"><p class="section-kicker">02 / Features</p><h2>${escapeHtml(c.featuresTitle)}</h2></div><ul class="feature-grid">${featureCards}</ul></div></section>
<section class="section process-section"><div class="container process-grid"><div><p class="section-kicker">03 / ${escapeHtml(c.processEyebrow)}</p><h2>${escapeHtml(c.processTitle)}</h2><p class="process-text">${escapeHtml(c.processText)}</p><a class="text-link" href="${guide}">${escapeHtml(c.guideLabel)} →</a></div><ol class="process-steps">${c.processSteps.map((step, index) => `<li><span>0${index + 1}</span><strong>${escapeHtml(step)}</strong></li>`).join('')}</ol></div></section>
<section class="section platform-section"><div class="container"><div class="section-intro"><p class="section-kicker">04 / Platforms</p><h2>${escapeHtml(c.platformsTitle)}</h2></div><div class="platform-grid"><div class="platform-card"><span class="platform-symbol" aria-hidden="true">▦</span><h3>${escapeHtml(c.windowsTitle)}</h3><p>${escapeHtml(c.windowsText)}</p></div><div class="platform-card"><span class="platform-symbol" aria-hidden="true">⌘</span><h3>${escapeHtml(c.macTitle)}</h3><p>${escapeHtml(c.macText)}</p></div></div></div></section>
<section class="section final-cta"><div class="container final-cta-inner"><div><p class="section-kicker">05 / Guide</p><h2>${escapeHtml(c.guideTitle)}</h2><p>${escapeHtml(c.guideText)}</p></div><a class="button button-primary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div></section>`;
}
