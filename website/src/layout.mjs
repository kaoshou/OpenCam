// SPDX-License-Identifier: AGPL-3.0-or-later
import { assetPath, routeFor } from './paths.mjs';

export function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
}

export function renderLayout({ language, title, description, page, body }) {
  const other = language === 'zh-TW' ? 'en-US' : 'zh-TW';
  const home = routeFor(language, 'home');
  const guide = routeFor(language, 'guide');
  const otherRoute = routeFor(other, page);
  const guideLabel = language === 'zh-TW' ? '使用說明' : 'User guide';
  const skip = language === 'zh-TW' ? '跳至主要內容' : 'Skip to content';
  const licenseLabel = language === 'zh-TW' ? '授權：AGPL-3.0-or-later' : 'License: AGPL-3.0-or-later';
  return `<!doctype html>
<html lang="${language}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${escapeHtml(title)} · OpenCam</title><meta name="description" content="${escapeHtml(description)}">
<link rel="icon" href="${assetPath('app_icon.png')}"><link rel="stylesheet" href="${assetPath('site.css')}">
<link rel="alternate" hreflang="${other}" href="${otherRoute}"><link rel="canonical" href="https://kaoshou.github.io${routeFor(language, page)}">
<script src="${assetPath('site.js')}" defer></script></head><body>
<a class="skip-link" href="#main">${skip}</a>
<header class="site-header"><nav class="nav-wrap" aria-label="${language === 'zh-TW' ? '主要導覽' : 'Main navigation'}"><a class="brand" href="${home}"><img src="${assetPath('app_icon.png')}" alt="OpenCam" width="32" height="32">OpenCam</a><div class="nav-links"><a class="home-link" href="${home}">${language === 'zh-TW' ? '首頁' : 'Home'}</a><a class="guide-link" href="${guide}">${guideLabel}</a><a class="github-link" href="https://github.com/kaoshou/OpenCam" target="_blank" rel="noopener noreferrer">GitHub</a><a class="language-switch" href="${otherRoute}" data-language="${other}">${other === 'zh-TW' ? '繁體中文' : 'English'}</a></div></nav></header>
<main id="main">${body}</main>
<footer class="site-footer"><div class="container"><span>© OpenCam · 鄭郁翰 (Yu-Han Cheng)</span><a href="https://github.com/kaoshou/OpenCam">GitHub</a><a href="${guide}">${guideLabel}</a><a href="https://github.com/kaoshou/OpenCam/blob/master/LICENSE">${licenseLabel}</a></div></footer></body></html>`;
}

export function renderEntry() {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpenCam — Choose a language</title><link rel="stylesheet" href="${assetPath('site.css')}"><link rel="icon" href="${assetPath('app_icon.png')}"><script src="${assetPath('site.js')}" defer></script></head><body><main id="main" class="language-entry"><img src="${assetPath('app_icon.png')}" alt="OpenCam" width="72" height="72"><h1>OpenCam</h1><p>Choose a language / 選擇語言</p><p><a href="${routeFor('zh-TW', 'home')}" data-language="zh-TW">繁體中文</a> <a href="${routeFor('en-US', 'home')}" data-language="en-US">English</a></p></main><footer class="site-footer"><div class="container"><span>© OpenCam · 鄭郁翰 (Yu-Han Cheng)</span></div></footer></body></html>`;
}
