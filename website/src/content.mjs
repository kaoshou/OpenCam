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
    heroText: '錄影、剪輯、續錄，一次完成。整理片段、附加多語字幕，再輸出 MP4 或具章節與逐字稿搜尋的網頁播放器。原始 MKV 素材保留在專案中。',
    downloadLabel: '下載 OpenCam', guideLabel: '閱讀使用說明',
    openSource: '開放原始碼 · 免費使用',
    licenseReleaseNote: `此頁介紹 ${displayVersion} 原始碼功能；Windows 實機驗收、瀏覽器視覺驗收與 FFmpeg 來源核對仍待完成。已發布版本請至 Releases 下載，發布不代表全部驗收通過。AGPL-3.0-or-later。`,
    screenshotTitle: '每一步，都清楚直覺。',
    screenshotText: '從開始錄影到整理片段，保留熟悉而簡單的操作。錄影中分別查看兩種收音波形；需要剪輯時，再開啟時間軸。點選圖片可在頁內放大檢視。',
    mainAlt: `OpenCam ${mainScreenshotVersion} macOS 繁體中文錄影中主畫面，右側顯示系統聲音與麥克風收音波形`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS 繁體中文偏好設定視窗`,
    mainCaption: `錄影中與收音波形 · macOS ${mainScreenshotVersion}`, settingsCaption: `偏好設定 · macOS ${mainScreenshotVersion}`,
    editorTitle: '錄好的內容，輕鬆剪輯。',
    editorText: '上方預覽、下方時間軸，搭配縮圖與波形定位。分割、裁剪或刪除不需要的部分，也能拖曳左側片段清單調整順序。儲存專案不必立即輸出 MP4，下次可從首頁開啟並追加錄製。',
    editorAlt: `OpenCam ${mainScreenshotVersion} 繁體中文剪輯器，顯示教學簡報影片、片段縮圖、音訊波形與時間軸`,
    editorCaption: `編輯錄製內容 · ${mainScreenshotVersion} · 自製教學影片`,
    editorPoints: ['拖曳時間尺持續預覽，放開後精確定位', '固定起點／終點旗標，標記本身不裁剪影片', '緊湊控制列與字幕選單；儲存、輸出分開操作'],
    subtitlesTitle: '讓教材看得懂，也找得到。',
    subtitlesText: '選取或拖放 SRT／VTT，多種語言保存在專案內，同名檔不覆蓋。在清單勾選預設字幕，預覽即可切換語言與外觀；網頁播放器另有逐字稿全文搜尋、點擊跳轉與跟隨播放。字幕不燒入 MP4。保存字幕使用專案格式4，舊版不能開啟，請先備份。',
    subtitlesAlt: `OpenCam ${mainScreenshotVersion} 繁體中文字幕管理，顯示多語卡片、語系代碼及預設單選`,
    subtitlesCaption: `附加多語字幕 · ${mainScreenshotVersion}`,
    fullSizeLabel: '放大檢視圖片',
    featuresTitle: '簡單上手，細節也到位。',
    features: [
      ['01', '指定螢幕或自訂區域', '辨識每台顯示器，或用透明、可調整的選取框圈出錄影範圍。'],
      ['02', '分開監看兩種聲音', '系統聲音與麥克風可獨立開關；分別顯示即時音量波形。可開啟麥克風連續無聲 30 秒提醒，提示不會中斷錄影。'],
      ['03', '時間軸剪輯', '用縮圖與波形定位，分割、裁剪、刪除範圍並重排片段。'],
      ['04', '保存專案，下次續錄', '可只儲存專案、不輸出 MP4；日後重新開啟，再追加錄影與編輯。'],
      ['05', '防誤關與自動回退', '錄影中關閉視窗會先提醒；硬體編碼不可用時可回退 CPU。'],
      ['06', '修復救援', '意外中斷後，可嘗試救回已寫入的 MKV 錄影片段。'],
      ['07', '網頁播放器輸出', '包含多語字幕、章節起始與長度、音量、速度、寬螢幕和全螢幕；可離線開啟或整包上傳網站。'],
      ['08', '逐字稿搜尋', '搜尋已附加字幕，標示關鍵字並跳到該時間；可跟隨播放或自由閱讀，清單限高捲動。'],
      ['09', '錄前確認與試錄', '檢查預設開啟，可在設定關閉。預覽直接顯示縮小影格，僅供確認範圍；試錄區分啟動、錄製與準備回放，五秒從確認啟動開始計時。可看已勾選音源音量並回放，不加入專案。實際延遲依設備而定。'],
      ['10', '快捷鍵標記重點', '錄影時按按鈕或自訂快捷鍵；編輯器可定位、改名與刪除，滑鼠停在紫色旗標可查看完整名稱，不改寫原始影片。']
    ],
    processEyebrow: '錄影機制', processTitle: '先保留錄影，再交付 MP4。',
    processText: '錄影先寫入 MKV，專案保留原始素材與剪輯決策。相容的完整片段可保留影片直接封裝或串接，必要時僅處理音訊；缺少影格長度資訊時，會先核對時間戳與尾端間隔。裁剪、效果或無法確認的素材會重新編碼。介面顯示實際處理階段，封裝後仍需校驗，輸出時間依內容而定。',
    processSteps: ['錄製並保留 MKV', '儲存或繼續編輯', '下次開啟並續錄', '驗證並輸出 MP4'],
    platformsTitle: '選擇你的平台', windowsTitle: 'Windows', windowsText: 'Windows 10／11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 以上 · Apple Silicon（arm64）',
    guideTitle: '第一次使用？從這裡開始。', guideText: '完整說明錄影、剪輯、儲存與續錄、MP4 輸出和修復救援。',
    previewNote: `圖片由 ${mainScreenshotDisplayVersion} 程式介面離線渲染；錄影狀態為示範資料，剪輯器使用自製影片。這些圖片不是實機收音或跨平台驗收證據。Windows 介面與裝置選項可能不同。`
  },
  'en-US': {
    eyebrow: 'Simple recording and editing for Windows and macOS',
    heroTitle: '<span class="hero-line">Capture what matters.</span><em class="hero-line">Keep it safe.</em>',
    heroText: 'Record, edit and continue in one place. Organize clips, attach multilingual subtitles and export MP4 or a web player with chapters and transcript search. Keep original MKV media in your project.',
    downloadLabel: 'Download OpenCam', guideLabel: 'Read the user guide',
    openSource: 'Open source · Free to use',
    licenseReleaseNote: `This page describes ${displayVersion} source features. Windows device acceptance, browser visual acceptance and FFmpeg source verification remain pending. Download published versions from Releases; publication does not establish complete acceptance. AGPL-3.0-or-later.`,
    screenshotTitle: 'Clear from the first click.',
    screenshotText: 'Keep recording simple, then open the timeline when you need to edit. Separate audio waveforms help monitor capture. Enlarge images in place without leaving the page.',
    mainAlt: `OpenCam ${mainScreenshotVersion} macOS English Recording screen with live system-audio and microphone audio waveforms`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS English preferences window`,
    mainCaption: `Recording with audio waveforms · macOS ${mainScreenshotVersion}`, settingsCaption: `Preferences · macOS ${mainScreenshotVersion}`,
    editorTitle: 'Turn your recording into a clearer story.',
    editorText: 'Preview above, timeline below. Use thumbnails and waveforms to find the right moment, split and trim clips, remove a range, or drag the clip list to reorder. Save the project without exporting, then reopen it from home to append another recording.',
    editorAlt: `OpenCam ${mainScreenshotVersion} English editor with an authored tutorial video, clip thumbnails, audio waveforms and timeline`,
    editorCaption: `Edit recording · ${mainScreenshotVersion} · Authored tutorial video`,
    editorPoints: ['Progressive ruler scrubbing; settle on release', 'Fixed IN/OUT flags; marking never trims media', 'Compact controls and caption menu; save and export separately'],
    subtitlesTitle: 'Make lessons easier to follow and find.',
    subtitlesText: 'Select or drop SRT/VTT files. Unique project-owned copies preserve different languages without filename collisions. Choose the default on a card and preview languages/styles. Web exports include Transcript search, click-to-seek and optional follow mode. No MP4 burn-in. Saving subtitles uses project format4, incompatible with older versions; back up first.',
    subtitlesAlt: `OpenCam ${mainScreenshotVersion} English subtitle manager with multilingual cards, language codes and a default radio choice`,
    subtitlesCaption: `Attach multilingual subtitles · ${mainScreenshotVersion}`,
    fullSizeLabel: 'Enlarge image',
    featuresTitle: 'Easy to start. Ready for the details.',
    features: [
      ['01', 'Display or custom region', 'Identify every connected display, or frame a region with the transparent, resizable selector.'],
      ['02', 'Monitor audio separately', 'Switch audio sources independently and see separate live levels. Optional warnings after 30 seconds of microphone silence never interrupt recording.'],
      ['03', 'Timeline editing', 'Locate content with thumbnails and waveforms, then split, trim, delete ranges and reorder clips.'],
      ['04', 'Save now, record again later', 'Save a project without exporting MP4, reopen it later and append more recordings.'],
      ['05', 'Close protection and fallback', 'Closing during recording prompts a warning; unavailable hardware encoding can fall back to CPU.'],
      ['06', 'Crash Recovery', 'After an interruption, attempt to salvage MKV segments already written.'],
      ['07', 'Web player export', 'Package multilingual captions, chapters, volume, speed, wide-screen and fullscreen controls. Open offline or upload the entire folder.'],
      ['08', 'Transcript search', 'Search attached captions, highlight matches and jump to their timestamps. Follow playback or read freely in a bounded scrolling list.'],
      ['09', 'Preflight and trial recording', 'Default-on checks can be disabled in Settings. Direct scaled frames help confirm the capture area. Trials distinguish startup, recording and preparing playback, timing five seconds after acknowledgement. Selected-input levels and replay stay outside the project. Device latency varies.'],
      ['10', 'Bookmark with a shortcut', 'Add bookmarks with a button or configurable shortcut; seek, rename or remove them in the editor. Hover over purple timeline flags for full names without rewriting video.']
    ],
    processEyebrow: 'How recording works', processTitle: 'Preserve first. Deliver MP4 afterward.',
    processText: 'Recordings are written to MKV; projects retain source media and editing decisions. Compatible whole segments preserve video through packaging or joining, processing only audio when needed. Missing video durations require ordered timestamps and a checked final interval. Trims, effects or uncertain sources retain re-encoding. The interface shows the actual stage; packaging still needs verification, and export time depends on the content.',
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
  const subtitlesImage = assetPath(`preview_subtitles_${suffix}.png`);
return `<section class="hero"><div class="container hero-grid"><div class="hero-copy"><p class="eyebrow"><span class="eyebrow-dot"></span>${escapeHtml(c.eyebrow)}</p><h1>${c.heroTitle}</h1><p class="hero-text">${escapeHtml(c.heroText)}</p><div class="hero-actions"><a class="button button-primary" href="${latest}">${escapeHtml(c.downloadLabel)} <span aria-hidden="true">↗</span></a><a class="button button-secondary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div><p class="hero-meta">${escapeHtml(c.openSource)}</p><p class="release-license-note">${escapeHtml(c.licenseReleaseNote)}</p></div><div class="hero-visual"><div class="window-halo"></div><a class="screenshot-link" href="${mainImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" width="840" height="820"></a><span class="visual-caption">${escapeHtml(c.mainCaption)} · ${escapeHtml(c.fullSizeLabel)}</span></div></div></section>
<section class="section screenshot-section" id="preview"><div class="container"><div class="section-intro"><p class="section-kicker">01 / OpenCam</p><h2>${escapeHtml(c.screenshotTitle)}</h2><p>${escapeHtml(c.screenshotText)}</p></div><div class="screenshot-grid"><figure class="screenshot-card"><a class="screenshot-link" href="${mainImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" loading="lazy" width="840" height="820"></a><figcaption>${escapeHtml(c.mainCaption)} · <a href="${mainImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><figure class="screenshot-card settings-card"><a class="screenshot-link" href="${settingsImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${settingsImage}" alt="${escapeHtml(c.settingsAlt)}" loading="lazy" width="650" height="820"></a><figcaption>${escapeHtml(c.settingsCaption)} · <a href="${settingsImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div><p class="preview-note">${escapeHtml(c.previewNote)}</p></div></section>
<section class="section editor-section" id="editing"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(mainScreenshotVersion)}</p><h2>${escapeHtml(c.editorTitle)}</h2><p>${escapeHtml(c.editorText)}</p></div><figure class="screenshot-card editor-card"><a class="screenshot-link" href="${editorImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${editorImage}" alt="${escapeHtml(c.editorAlt)}" loading="lazy" width="1440" height="900"></a><figcaption>${escapeHtml(c.editorCaption)} · <a href="${editorImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><ul class="editor-highlights">${c.editorPoints.map(point => `<li>${escapeHtml(point)}</li>`).join('')}</ul></div></section>
<section class="section editor-section" id="subtitles"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(productVersion)}</p><h2>${escapeHtml(c.subtitlesTitle)}</h2><p>${escapeHtml(c.subtitlesText)}</p></div><figure class="screenshot-card editor-card"><a class="screenshot-link" href="${subtitlesImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${subtitlesImage}" alt="${escapeHtml(c.subtitlesAlt)}" loading="lazy" width="780" height="620"></a><figcaption>${escapeHtml(c.subtitlesCaption)} · <a href="${subtitlesImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div></section>
<section class="section features-section" id="features"><div class="container"><div class="section-intro"><p class="section-kicker">02 / Features</p><h2>${escapeHtml(c.featuresTitle)}</h2></div><ul class="feature-grid">${featureCards}</ul></div></section>
<section class="section process-section"><div class="container process-grid"><div><p class="section-kicker">03 / ${escapeHtml(c.processEyebrow)}</p><h2>${escapeHtml(c.processTitle)}</h2><p class="process-text">${escapeHtml(c.processText)}</p><a class="text-link" href="${guide}">${escapeHtml(c.guideLabel)} →</a></div><ol class="process-steps">${c.processSteps.map((step, index) => `<li><span>0${index + 1}</span><strong>${escapeHtml(step)}</strong></li>`).join('')}</ol></div></section>
<section class="section platform-section"><div class="container"><div class="section-intro"><p class="section-kicker">04 / Platforms</p><h2>${escapeHtml(c.platformsTitle)}</h2></div><div class="platform-grid"><div class="platform-card"><span class="platform-symbol" aria-hidden="true">▦</span><h3>${escapeHtml(c.windowsTitle)}</h3><p>${escapeHtml(c.windowsText)}</p></div><div class="platform-card"><span class="platform-symbol" aria-hidden="true">⌘</span><h3>${escapeHtml(c.macTitle)}</h3><p>${escapeHtml(c.macText)}</p></div></div></div></section>
<section class="section final-cta"><div class="container final-cta-inner"><div><p class="section-kicker">05 / Guide</p><h2>${escapeHtml(c.guideTitle)}</h2><p>${escapeHtml(c.guideText)}</p></div><a class="button button-primary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div></section>`;
}
