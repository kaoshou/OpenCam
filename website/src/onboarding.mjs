// SPDX-License-Identifier: AGPL-3.0-or-later
import { assetPath, routeFor } from './paths.mjs';
import { escapeHtml } from './layout.mjs';

export function renderOnboarding(language) {
  const zh = language === 'zh-TW';
  const styles = zh
    ? [['WarmIvory','暖白橙端','暖色尖端，適合一般教材。'],['TealSilver','藍綠銀白','冷色輪廓，適合簡報指示。'],['RoundShort','圓頭短棒','短棒造型，減少遮住文字。'],['HighContrast','高對比黑金','深淺輪廓，方便辨識。']]
    : [['WarmIvory','Warm ivory','Warm tip for everyday lessons.'],['TealSilver','Teal silver','Cool outline for presentations.'],['RoundShort','Round short stick','Short design covers less text.'],['HighContrast','High-contrast gold','Light and dark outlines for visibility.']];
  return `<section class="section" id="quick-start"><div class="container">
    <div class="section-intro"><p class="section-kicker">${zh ? '錄影・編輯・分享' : 'Record · Edit · Share'}</p>
      <h2>${zh ? '簡單錄影，輕鬆製作教學影片。' : 'Simple recording. Lessons made easy.'}</h2>
      <p>${zh ? '在 Windows 與 macOS 錄製螢幕或自訂區域，自由搭配系統聲音與麥克風。使用虛擬教鞭指示重點，錄影途中可暫停、繼續，保留分段素材，方便之後編輯。' : 'Record a display or custom region on Windows and macOS, with system audio, microphone audio, or both. Highlight key points with a virtual teaching pointer, pause and resume, and keep separate recording segments for later editing.'}</p>
      <p>${zh ? '錄完可修剪、分割、合併片段與匯入多重字幕，再輸出 MP4、GIF 或網頁播放器。播放器提供章節導覽、字幕、逐字稿搜尋、閱讀設定及逐字稿下載，方便分享與複習。' : 'Trim, split and merge clips, attach multiple subtitle tracks, then export MP4, GIF or a Web player. The player offers chapters, captions, transcript search, reading settings and transcript downloads for sharing and review.'}</p>
    </div>
    <h3>${zh ? '找到熟悉的上手方式' : 'Start with a familiar workflow'}</h3>
    <p class="workflow-intro">${zh ? 'OpenCam 結合 oCam 簡單易用的錄影概念，以及 EverCam 製作數位教材的工作流程。以下提供相近體驗的設定方式，讓熟悉這兩款工具的使用者快速上手。' : 'OpenCam brings together the idea of straightforward recording familiar to oCam users and digital lesson workflows familiar to EverCam users. The settings below offer similar experiences to help you get started.'}</p>
    <div class="onboarding-grid">
      <article class="feature-card"><h3>${zh ? 'oCam 使用者：錄完即取檔' : 'For oCam users: record to file'}</h3><ol>${(zh ? ['選擇錄影範圍及需要的聲音來源。','勾選「停止錄影後自動輸出 MP4」。','開始錄影，完成後按停止，等待自動輸出影片。'] : ['Choose the recording area and audio inputs.','Enable Auto-export MP4 on stop.','Start recording, stop when finished, and wait for your video to export.']).map(s=>`<li>${escapeHtml(s)}</li>`).join('')}</ol><p>${zh ? '也可手動匯出 GIF（無音訊）；OpenCam 另保留專案及原始 MKV，方便續錄與編輯。' : 'You can also export GIF without audio. OpenCam keeps the project and original MKV media for further recording and editing.'}</p></article>
      <article class="feature-card"><h3>${zh ? 'EverCam 使用者：分段教學' : 'For EverCam users: segmented lessons'}</h3><ol>${(zh ? ['新增錄製專案，游標樣式選「虛擬教鞭」。','開始錄影；暫停／繼續，或停止後再開始，自動分段。','編輯錄製內容：分割、合併、修剪，匯入多重字幕。','輸出 MP4 或網頁播放器。'] : ['Create a project and select Presentation stick.','Record, pause/resume, or stop and start again to create segments.','Edit: split, merge, trim and attach multiple subtitle tracks.','Export MP4 or a Web player.']).map(s=>`<li>${escapeHtml(s)}</li>`).join('')}</ol></article>
    </div>
    <p class="trademark-note">${zh ? 'oCam、EverCam 名稱及相關商標屬各自權利人所有；此處僅作使用方式說明，無合作或背書關係。' : 'oCam, EverCam and their trademarks belong to their respective owners. References describe workflows only; no affiliation or endorsement is implied.'}</p>
    <h3>${zh ? '選一支適合教材的教鞭' : 'Choose a teaching pointer'}</h3>
    <ul class="pointer-showcase">${styles.map(([id,name,description])=>`<li class="feature-card"><img src="${assetPath(`pointer-${id}.png`)}" width="96" height="96" alt="${escapeHtml(name)}" loading="lazy"><h4>${escapeHtml(name)}</h4><p>${escapeHtml(description)}</p></li>`).join('')}</ul>
    <p>${zh ? '各樣式可選較小、標準、加大；尖端對準指示位置。教鞭直接錄入影片，事後不能更換。以上為相近工作流程，不代表其他軟體專案格式相容或具關聯。' : 'Each style offers Small, Standard and Large with a precise tip hotspot. Pointers are recorded into the video, not replaceable afterwards. Familiar workflows do not imply project-format compatibility or affiliation.'}</p>
    <a class="button button-secondary" href="${routeFor(language,'guide')}">${zh ? '查看快速使用與完整手冊' : 'Read the quick start and full guide'}</a>
  </div></section>`;
}
