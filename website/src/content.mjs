// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';
import { renderOnboarding } from './onboarding.mjs';

const versionText = readFileSync(new URL('../../VERSION', import.meta.url), 'utf8');
if (!/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\n$/.test(versionText)) {
  throw new Error('VERSION must contain numeric SemVer and exactly one final LF');
}
export const productVersion = versionText.slice(0, -1);
// Advance this manifest only after all three public Release assets are verified.
// Development VERSION changes must not silently create dead download links.
const downloads = JSON.parse(readFileSync(new URL('../downloads.json', import.meta.url), 'utf8'));
if (typeof downloads.version !== 'string' || !/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/.test(downloads.version)) {
  throw new Error('Download version must identify a numeric published release');
}
export const downloadVersion = downloads.version;
const downloadDisplayVersion = `v${downloadVersion}`;
export const mainScreenshotVersion = JSON.parse(readFileSync(new URL('../../docs/images/metadata.json', import.meta.url), 'utf8')).version;
const mainScreenshotDisplayVersion = `v${mainScreenshotVersion}`;

export const content = {
  'zh-TW': {
    eyebrow: 'Windows 與 macOS 的簡單錄影與剪輯工具',
    heroTitle: '<span class="hero-line">把重要的畫面，</span><em class="hero-line">好好錄下來。</em>',
    heroText: '錄影、剪輯、續錄，一次完成。整理片段、附加多語字幕，再輸出 MP4、GIF 或具章節與逐字稿搜尋的網頁播放器。原始 MKV 素材保留在專案中。',
    windowsPortableLabel: '下載 Windows 免安裝版', windowsInstallerLabel: '下載 Windows 安裝版', macOSLabel: '下載 macOS 版', guideLabel: '閱讀使用說明',
    windowsDownloadNote: 'Windows 適用於 x64；免安裝版請完整解壓後執行。',
    macOSDownloadNote: 'macOS 適用於 Apple Silicon（M 系列）。',
    releaseLinksLabel: '版本說明與其他下載：GitHub Releases',
    openSource: '開放原始碼 · 免費使用',
    licenseReleaseNote: `目前版本 ${downloadDisplayVersion} · Windows 與 macOS · AGPL-3.0-or-later`,
    screenshotTitle: '每一步，都清楚直覺。',
    screenshotText: '從開始錄影到整理片段，保留熟悉而簡單的操作。點選圖片可在頁內放大檢視。',
    mainAlt: `OpenCam ${mainScreenshotVersion} 繁體中文主畫面，顯示錄影範圍與教鞭選項`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS 繁體中文偏好設定視窗`,
    mainCaption: `錄影主畫面 · ${mainScreenshotVersion}`, settingsCaption: `偏好設定 · ${mainScreenshotVersion}`,
    editorTitle: '錄好的內容，輕鬆剪輯。',
    editorText: '上方預覽、下方時間軸，搭配縮圖與波形定位。分割、裁剪或刪除不需要的部分，也能拖曳左側片段清單調整順序。時間軸片段右鍵可更改名稱、刪除或匯出 MP4／GIF。程式介面支援繁體中文、英文及日文。',
    editorAlt: `OpenCam ${mainScreenshotVersion} 繁體中文剪輯器，顯示教學簡報影片、片段縮圖、音訊波形與時間軸`,
    editorCaption: `編輯錄製內容 · ${mainScreenshotVersion} · 自製教學影片`,
    editorPoints: ['拖曳時間尺持續預覽，放開後精確定位', '固定起點／終點旗標，標記本身不裁剪影片', '緊湊控制列與字幕選單；儲存、輸出分開操作'],
    subtitlesTitle: '讓教材看得懂，也找得到。',
    subtitlesText: '選取或拖放 SRT／VTT，多種語言保存在專案內，同名檔不覆蓋。在清單勾選預設字幕，預覽即可切換語言與外觀；網頁播放器提供逐字稿搜尋、跳轉與跟隨播放，另可調整閱讀字級、行距與背景，下載完整 HTML／TXT 閱讀版。字幕不燒入 MP4。保存字幕使用專案格式4，舊版不能開啟，請先備份。',
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
      ['09', '錄前聲音確認', '確認已選的系統聲音與麥克風，提供五秒聲音試錄與回放。獨立狀態區清楚顯示載入、試錄與正式錄製準備。'],
      ['10', '快捷鍵標記重點', '錄影時按按鈕或自訂快捷鍵；編輯器可定位、改名與刪除，滑鼠停在紫色旗標可查看完整名稱，不改寫原始影片。'],
      ['11', 'MP4 品質與 GIF 匯出', '整部影片、單一片段及網頁播放器的 MP4 可設定壓縮品質、原始／最高 1080p／最高 720p，保持比例、不放大並記住選擇。壓縮需重新編碼，檔案大小依內容而異。GIF 可選大小與 FPS、沒有音訊；達五分鐘會提醒並可繼續。'],
      ["12","OBS 核心錄影","Windows 使用 Windows Graphics Capture，macOS 使用 ScreenCaptureKit，不使用舊核心錄影備援；FFmpeg 保留於聲音試錄、預覽、剪輯與輸出。首次啟動預設 20 FPS，可依內容調整。"],
      ['13', '螢幕註記', '錄影時使用畫筆、直線、箭頭、方框與圓圈標示重點，可調整顏色、粗細，並提供橡皮擦、復原與可自訂快捷鍵。註記直接錄入影片。']
    ],
    processEyebrow: '錄影與輸出', processTitle: '保留原始錄影，自由選擇輸出。',
    processText: '錄影片段以 MKV 保存在專案內，剪輯不覆寫原始素材。隨時儲存、重開及追加錄影，再依需求輸出 MP4、GIF 或網頁播放器。相容的完整片段可快速輸出；修剪與效果會依內容進行轉換，介面會顯示目前進度。',
    processSteps: ['錄製並保留 MKV', '儲存或繼續編輯', '下次開啟並續錄', '輸出 MP4、GIF 或網頁'],
    platformsTitle: '選擇你的平台', windowsTitle: 'Windows', windowsText: 'Windows 10／11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 以上 · Apple Silicon（arm64）',
    guideTitle: '第一次使用？從這裡開始。', guideText: '完整說明錄影、剪輯、儲存與續錄、MP4／GIF／網頁輸出和修復救援。',
    previewNote: `圖片為 ${mainScreenshotDisplayVersion} 的介面示意，最新功能請參考使用說明。Windows 與 macOS 的裝置選項依平台顯示。`
  },
  'en-US': {
    eyebrow: 'Simple recording and editing for Windows and macOS',
    heroTitle: '<span class="hero-line">Capture what matters.</span><em class="hero-line">Keep it safe.</em>',
    heroText: 'Record, edit and continue in one place. Organize clips, attach multilingual subtitles and export MP4, GIF or a web player with chapters and transcript search. Keep original MKV media in your project.',
    windowsPortableLabel: 'Download Windows portable', windowsInstallerLabel: 'Download Windows installer', macOSLabel: 'Download macOS', guideLabel: 'Read the user guide',
    windowsDownloadNote: 'Windows requires x64. Fully extract the portable ZIP before running.',
    macOSDownloadNote: 'macOS requires Apple Silicon (M-series).',
    releaseLinksLabel: 'Release notes and other downloads: GitHub Releases',
    openSource: 'Open source · Free to use',
    licenseReleaseNote: `Current version ${downloadDisplayVersion} · Windows and macOS · AGPL-3.0-or-later`,
    screenshotTitle: 'Clear from the first click.',
    screenshotText: 'Keep recording simple, then open the timeline when you need to edit. Enlarge images in place without leaving the page.',
    mainAlt: `OpenCam ${mainScreenshotVersion} English recording screen with capture and teaching pointer settings`,
    settingsAlt: `OpenCam ${mainScreenshotVersion} macOS English preferences window`,
    mainCaption: `Recording screen · ${mainScreenshotVersion}`, settingsCaption: `Preferences · ${mainScreenshotVersion}`,
    editorTitle: 'Turn your recording into a clearer story.',
    editorText: 'Preview above, timeline below. Use thumbnails and waveforms to split, trim or remove clips, and drag the clip list to reorder. Right-click timeline clips to rename, delete or export MP4/GIF. The application interface supports Traditional Chinese, English and Japanese.',
    editorAlt: `OpenCam ${mainScreenshotVersion} English editor with an authored tutorial video, clip thumbnails, audio waveforms and timeline`,
    editorCaption: `Edit recording · ${mainScreenshotVersion} · Authored tutorial video`,
    editorPoints: ['Progressive ruler scrubbing; settle on release', 'Fixed IN/OUT flags; marking never trims media', 'Compact controls and caption menu; save and export separately'],
    subtitlesTitle: 'Make lessons easier to follow and find.',
    subtitlesText: 'Select or drop SRT/VTT files. Unique project-owned copies preserve different languages without filename collisions. Choose the default on a card and preview languages/styles. Web exports include Transcript search, click-to-seek, optional follow mode, reading text/spacing/background settings and full HTML/TXT downloads. No MP4 burn-in. Saving subtitles uses project format4, incompatible with older versions; back up first.',
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
      ['09', 'Audio preflight', 'Check selected system-audio and microphone inputs with a five-second audio trial and replay. A separate status area shows loading, trials and recording preparation.'],
      ['10', 'Bookmark with a shortcut', 'Add bookmarks with a button or configurable shortcut; seek, rename or remove them in the editor. Hover over purple timeline flags for full names without rewriting video.'],
      ['11', 'MP4 quality and GIF export', 'Choose MP4 compression and original/up to 1080p/up to 720p resolution for whole videos, individual clips or Web players. Choices are remembered, aspect ratio preserved and videos never upscaled. Compression re-encodes; size depends on content. GIF offers size/FPS options without audio, with confirmation at five minutes.'],
      ["12","OBS-core recording","Windows Graphics Capture on Windows and ScreenCaptureKit on macOS, without legacy recording fallback. FFmpeg remains for audio trials, preview, editing and export. First launch defaults to 20 FPS."],
      ['13', 'Screen annotations', 'Mark up the screen while recording with a pen, lines, arrows, rectangles and ellipses. Adjust colors and line widths, erase marks, undo and customize shortcuts. Annotations are recorded into the video.']
    ],
    processEyebrow: 'Recording and export', processTitle: 'Keep originals. Choose how to share.',
    processText: 'MKV recordings stay in your project while edits preserve the original media. Save, reopen and append recordings, then export MP4, GIF or a Web player. Compatible whole clips support fast export; trims and effects are converted as needed, with progress shown in the interface.',
    processSteps: ['Record and retain MKV', 'Save or edit', 'Reopen and append', 'Export MP4, GIF or Web'],
    platformsTitle: 'Choose your platform', windowsTitle: 'Windows', windowsText: 'Windows 10/11 · x64',
    macTitle: 'macOS', macText: 'macOS 13 or later · Apple Silicon (arm64)',
    guideTitle: 'New to OpenCam? Start here.', guideText: 'A complete guide to recording, editing, saving, resuming, MP4/GIF/Web export and Crash Recovery.',
    previewNote: `Illustrations show the ${mainScreenshotDisplayVersion} interface. See the user guide for current features. Device options follow the Windows or macOS platform.`
  }
};

export function renderHome(language) {
  const c = content[language];
  if (!c) throw new TypeError('Unsupported site language');
  const suffix = language === 'zh-TW' ? 'zhtw' : 'enus';
  const guide = routeFor(language, 'guide');
  const latest = 'https://github.com/kaoshou/OpenCam/releases';
  const downloadBase = `${latest}/download/${downloadDisplayVersion}/OpenCam_${downloadDisplayVersion}`;
  const featureCards = c.features.map(([number, title, text]) => `<li class="feature-card"><span class="feature-number">${number}</span><h3>${escapeHtml(title)}</h3><p>${escapeHtml(text)}</p></li>`).join('');
  const mainImage = assetPath(`preview_main_${suffix}.png`);
  const settingsImage = assetPath(`preview_settings_${suffix}.png`);
  const editorImage = assetPath(`preview_editor_${suffix}.png`);
  const subtitlesImage = assetPath(`preview_subtitles_${suffix}.png`);
  const annotationImage = assetPath('preview_annotations.png');
  const annotationToolbar = assetPath(`preview_annotation_toolbar_${suffix}.png`);
return `<section class="hero"><div class="container hero-grid"><div class="hero-copy"><p class="eyebrow"><span class="eyebrow-dot"></span>${escapeHtml(c.eyebrow)}</p><h1>${c.heroTitle}</h1><p class="hero-text">${escapeHtml(c.heroText)}</p><div class="hero-actions"><a class="button button-primary" href="${downloadBase}_Windows_x64_Portable.zip">${escapeHtml(c.windowsPortableLabel)}</a><a class="button button-secondary" href="${downloadBase}_Windows_x64_Setup.exe">${escapeHtml(c.windowsInstallerLabel)}</a><a class="button button-secondary" href="${downloadBase}_macOS_arm64.dmg">${escapeHtml(c.macOSLabel)}</a></div><div class="download-notes"><p>${escapeHtml(c.windowsDownloadNote)}</p><p>${escapeHtml(c.macOSDownloadNote)}</p><p class="download-links"><a href="${latest}">${escapeHtml(c.releaseLinksLabel)}</a><a href="${guide}">${escapeHtml(c.guideLabel)}</a></p></div><p class="hero-meta">${escapeHtml(c.openSource)}</p><p class="release-license-note">${escapeHtml(c.licenseReleaseNote)}</p></div><div class="hero-visual"><div class="window-halo"></div><a class="screenshot-link" href="${mainImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" width="1440" height="1000"></a><span class="visual-caption">${escapeHtml(c.mainCaption)} · ${escapeHtml(c.fullSizeLabel)}</span></div></div></section>
${renderOnboarding(language)}
<section class="section"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(productVersion)}</p><h2>${language === 'zh-TW' ? '錄影時，直接標示重點。' : 'Mark up your screen while recording.'}</h2><p>${language === 'zh-TW' ? '使用畫筆、線條與圖形標示教材，可調整顏色和粗細。註記直接錄入影片，工具列也可能出現在錄影中。' : 'Use a pen, lines and shapes to highlight teaching material, with adjustable colors and line widths. Annotations are recorded into the video, and the toolbar may also be captured.'}</p></div><figure class="screenshot-card"><img src="${annotationImage}" alt="${language === 'zh-TW' ? '螢幕註記示意：方框、圓圈與箭頭' : 'Screen annotation illustration with rectangle, ellipse and arrow'}" loading="lazy"><figcaption>${language === 'zh-TW' ? '以程式繪圖元件製作的註記示意' : 'Annotation illustration rendered with the application drawing component'}</figcaption></figure><figure class="screenshot-card"><img src="${annotationToolbar}" alt="${language === 'zh-TW' ? '螢幕註記工具列' : 'Screen annotation toolbar'}" loading="lazy"><figcaption>${language === 'zh-TW' ? '可拖曳及隱藏的螢幕註記工具列' : 'Movable screen annotation toolbar'}</figcaption></figure></div></section>
<section class="section screenshot-section" id="preview"><div class="container"><div class="section-intro"><p class="section-kicker">01 / OpenCam</p><h2>${escapeHtml(c.screenshotTitle)}</h2><p>${escapeHtml(c.screenshotText)}</p></div><div class="screenshot-grid"><figure class="screenshot-card"><a class="screenshot-link" href="${mainImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${mainImage}" alt="${escapeHtml(c.mainAlt)}" loading="lazy" width="1440" height="1000"></a><figcaption>${escapeHtml(c.mainCaption)} · <a href="${mainImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><figure class="screenshot-card settings-card"><a class="screenshot-link" href="${settingsImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${settingsImage}" alt="${escapeHtml(c.settingsAlt)}" loading="lazy" width="650" height="820"></a><figcaption>${escapeHtml(c.settingsCaption)} · <a href="${settingsImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div><p class="preview-note">${escapeHtml(c.previewNote)}</p></div></section>
<section class="section editor-section" id="editing"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(mainScreenshotVersion)}</p><h2>${escapeHtml(c.editorTitle)}</h2><p>${escapeHtml(c.editorText)}</p></div><figure class="screenshot-card editor-card"><a class="screenshot-link" href="${editorImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${editorImage}" alt="${escapeHtml(c.editorAlt)}" loading="lazy" width="1600" height="1000"></a><figcaption>${escapeHtml(c.editorCaption)} · <a href="${editorImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure><ul class="editor-highlights">${c.editorPoints.map(point => `<li>${escapeHtml(point)}</li>`).join('')}</ul></div></section>
<section class="section editor-section" id="subtitles"><div class="container"><div class="section-intro"><p class="section-kicker">NEW / ${escapeHtml(productVersion)}</p><h2>${escapeHtml(c.subtitlesTitle)}</h2><p>${escapeHtml(c.subtitlesText)}</p></div><figure class="screenshot-card editor-card"><a class="screenshot-link" href="${subtitlesImage}" data-lightbox aria-label="${escapeHtml(c.fullSizeLabel)}"><img src="${subtitlesImage}" alt="${escapeHtml(c.subtitlesAlt)}" loading="lazy" width="780" height="620"></a><figcaption>${escapeHtml(c.subtitlesCaption)} · <a href="${subtitlesImage}" data-lightbox>${escapeHtml(c.fullSizeLabel)}</a></figcaption></figure></div></section>
<section class="section features-section" id="features"><div class="container"><div class="section-intro"><p class="section-kicker">02 / Features</p><h2>${escapeHtml(c.featuresTitle)}</h2></div><ul class="feature-grid">${featureCards}</ul></div></section>
<section class="section process-section"><div class="container process-grid"><div><p class="section-kicker">03 / ${escapeHtml(c.processEyebrow)}</p><h2>${escapeHtml(c.processTitle)}</h2><p class="process-text">${escapeHtml(c.processText)}</p><a class="text-link" href="${guide}">${escapeHtml(c.guideLabel)} →</a></div><ol class="process-steps">${c.processSteps.map((step, index) => `<li><span>0${index + 1}</span><strong>${escapeHtml(step)}</strong></li>`).join('')}</ol></div></section>
<section class="section platform-section"><div class="container"><div class="section-intro"><p class="section-kicker">04 / Platforms</p><h2>${escapeHtml(c.platformsTitle)}</h2></div><div class="platform-grid"><div class="platform-card"><span class="platform-symbol" aria-hidden="true">▦</span><h3>${escapeHtml(c.windowsTitle)}</h3><p>${escapeHtml(c.windowsText)}</p></div><div class="platform-card"><span class="platform-symbol" aria-hidden="true">⌘</span><h3>${escapeHtml(c.macTitle)}</h3><p>${escapeHtml(c.macText)}</p></div></div></div></section>
<section class="section final-cta"><div class="container final-cta-inner"><div><p class="section-kicker">05 / Guide</p><h2>${escapeHtml(c.guideTitle)}</h2><p>${escapeHtml(c.guideText)}</p></div><a class="button button-primary" href="${guide}">${escapeHtml(c.guideLabel)} <span aria-hidden="true">→</span></a></div></section>`;
}
