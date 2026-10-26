// SPDX-License-Identifier: AGPL-3.0-or-later
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';

export function renderOnboarding(language) {
  const zh = language === 'zh-TW';
  const styles = zh
    ? [['WarmIvory','暖白橙端','暖色尖端，適合一般教材。'],['TealSilver','藍綠銀白','冷色輪廓，適合簡報指示。'],['RoundShort','圓頭短棒','短棒造型，減少遮住文字。'],['HighContrast','高對比黑金','深淺輪廓，方便辨識。']]
    : [['WarmIvory','Warm ivory','Warm tip for everyday lessons.'],['TealSilver','Teal silver','Cool outline for presentations.'],['RoundShort','Round short stick','Short design covers less text.'],['HighContrast','High-contrast gold','Light and dark outlines for visibility.']];
  return `<section class="section" id="quick-start"><div class="container">
    <div class="section-intro"><p class="section-kicker">v1.0.0</p>
      <h2>${zh ? '重大更新，熟悉的錄影方式。' : 'A major update. A familiar workflow.'}</h2>
      <p>${zh ? 'Windows 與 macOS 改用 OBS／libobs 錄影核心，針對舊流程影音不同步、實際 FPS 偏低及卡頓改善擷取與時間軸。新增虛擬教鞭，讓數位教學更容易錄製。實際效果依硬體、解析度及負載而異，首次啟動預設 20 FPS。' : 'Windows and macOS now record through OBS/libobs to address A/V synchronization, low achieved FPS and stutter in the previous pipeline. Virtual teaching pointers simplify lesson recording. Performance depends on hardware, resolution and load; first launch defaults to 20 FPS.'}</p>
      <p>${zh ? '網頁播放器提供鍵盤導覽、Accesskey、閱讀設定及可下載逐字稿。' : 'The Web player offers keyboard navigation, accesskeys, reading settings and downloadable transcripts.'}</p>
    </div>
    <p class="workflow-intro">${zh ? 'OpenCam 結合 oCam 簡單易用的錄影概念，以及 EverCam 製作數位教材的工作流程。以下提供相近體驗的設定方式，讓熟悉這兩款工具的使用者快速上手。' : 'OpenCam brings together the idea of straightforward recording familiar to oCam users and digital lesson workflows familiar to EverCam users. The settings below offer similar experiences to help you get started.'}</p>
    <div class="onboarding-grid">
      <article class="feature-card"><h3>${zh ? 'oCam 使用者：錄完即取檔' : 'For oCam users: record to file'}</h3><p>${zh ? '勾選「停止錄影後自動輸出 MP4」，錄完停止並等待輸出，即可取得影片。也支援手動匯出 GIF（無音訊），另外保留專案及原始 MKV。' : 'Enable Auto-export MP4 on stop. Record, stop and wait for your video. You can also export GIF without audio, while keeping your project and original MKV media.'}</p></article>
      <article class="feature-card"><h3>${zh ? 'EverCam 使用者：分段教學' : 'For EverCam users: segmented lessons'}</h3><ol>${(zh ? ['新增錄製專案，游標樣式選「虛擬教鞭」。','開始錄影；暫停／繼續，或停止後再開始，自動分段。','編輯錄製內容：分割、合併、修剪，匯入多重字幕。','輸出 MP4 或網頁播放器。'] : ['Create a project and select Presentation stick.','Record, pause/resume, or stop and start again to create segments.','Edit: split, merge, trim and attach multiple subtitle tracks.','Export MP4 or a Web player.']).map(s=>`<li>${escapeHtml(s)}</li>`).join('')}</ol></article>
    </div>
    <p class="trademark-note">${zh ? 'oCam、EverCam 名稱及相關商標屬各自權利人所有；此處僅作使用方式說明，無合作或背書關係。' : 'oCam, EverCam and their trademarks belong to their respective owners. References describe workflows only; no affiliation or endorsement is implied.'}</p>
    <h3>${zh ? '選一支適合教材的教鞭' : 'Choose a teaching pointer'}</h3>
    <ul class="pointer-showcase">${styles.map(([id,name,description])=>`<li class="feature-card"><img src="${assetPath(`pointer-${id}.png`)}" width="96" height="96" alt="${escapeHtml(name)}" loading="lazy"><h4>${escapeHtml(name)}</h4><p>${escapeHtml(description)}</p></li>`).join('')}</ul>
    <p>${zh ? '各樣式可選較小、標準、加大；尖端對準指示位置。教鞭直接錄入影片，事後不能更換。以上為相近工作流程，不代表其他軟體專案格式相容或具關聯。' : 'Each style offers Small, Standard and Large with a precise tip hotspot. Pointers are recorded into the video, not replaceable afterwards. Familiar workflows do not imply project-format compatibility or affiliation.'}</p>
    <a class="button button-secondary" href="${routeFor(language,'guide')}">${zh ? '查看快速使用與完整手冊' : 'Read the quick start and full guide'}</a>
  </div></section>`;
}
