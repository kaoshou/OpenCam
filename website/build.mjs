import { mkdir, readFile, writeFile, copyFile, stat } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderEntry, renderLayout } from './src/layout.mjs';
import { renderHome, content } from './src/content.mjs';
import { renderGuide } from './src/guide.mjs';
import { escapeHtml } from './src/layout.mjs';

const websiteRoot = fileURLToPath(new URL('.', import.meta.url));
const requiredAssets = [
  ['src/ScreenRecorder.UI/Assets/app_icon.png', 'app_icon.png'],
  ['docs/images/preview_main_zhtw.png', 'preview_main_zhtw.png'],
  ['docs/images/preview_main_enus.png', 'preview_main_enus.png'],
  ['docs/images/preview_settings_zhtw.png', 'preview_settings_zhtw.png'],
  ['docs/images/preview_settings_enus.png', 'preview_settings_enus.png']
];
const routeFiles = ['index.html', 'zh-TW/index.html', 'en-US/index.html', 'zh-TW/guide/index.html', 'en-US/guide/index.html'];

export async function validateOutput(outputRoot) {
  for (const route of routeFiles) {
    const html = await readFile(join(outputRoot, route), 'utf8');
    if ((html.match(/<main\b/g) || []).length !== 1) throw new Error(`Expected one main landmark: ${route}`);
    if (route.includes('/guide/') && !/<nav class="guide-toc"(?:\s|>)/.test(html)) throw new Error(`Missing guide contents: ${route}`);
    for (const match of html.matchAll(/<img\b[^>]*>/g)) {
      if (!/\balt="[^"]+"/.test(match[0])) throw new Error(`Missing image alternative: ${route}`);
    }
    for (const match of html.matchAll(/\b(?:href|src)="([^"]+)"/g)) {
      const href = match[1];
      if (/^(https?:|mailto:|#)/i.test(href)) continue;
      if (!href.startsWith('/OpenCam/')) throw new Error(`Invalid project-site path: ${route}: ${href}`);
      const relative = decodeURIComponent(href.slice('/OpenCam/'.length).split('#')[0].split('?')[0]);
      const target = resolve(outputRoot, relative);
      if (target !== resolve(outputRoot) && !target.startsWith(resolve(outputRoot) + sep)) throw new Error(`Path escapes site: ${href}`);
      try {
        const info = await stat(target);
        if (info.isDirectory()) await required(join(target, 'index.html'));
        else if (info.size === 0) throw new Error('empty');
      } catch { throw new Error(`Unresolved site asset: ${route}: ${href}`); }
    }
  }
  for (const [, asset] of requiredAssets) await required(join(outputRoot, 'assets', asset));
}

async function required(path) {
  try {
    const info = await stat(path);
    if (!info.isFile() || info.size === 0) throw new Error('empty');
    return path;
  } catch {
    throw new Error(`Missing required site source: ${path}`);
  }
}

async function writePage(outputRoot, path, html) {
  const target = join(outputRoot, path, 'index.html');
  await mkdir(join(outputRoot, path), { recursive: true });
  await writeFile(target, html);
}

export async function buildSite({ repositoryRoot = resolve(websiteRoot, '..'), outputRoot = join(websiteRoot, 'dist') } = {}) {
  const sources = [...requiredAssets.map(([source]) => join(repositoryRoot, source)), join(repositoryRoot, 'docs/USER_GUIDE.zh-TW.md'), join(repositoryRoot, 'docs/USER_GUIDE.en-US.md'), join(websiteRoot, 'src/site.css'), join(websiteRoot, 'src/site.js')];
  await Promise.all(sources.map(required));
  await mkdir(join(outputRoot, 'assets'), { recursive: true });
  await writePage(outputRoot, '', renderEntry());
  for (const language of ['zh-TW', 'en-US']) {
    await writePage(outputRoot, language, renderLayout({ language, title: language === 'zh-TW' ? '簡單螢幕錄影' : 'Simple screen recording', description: content[language].heroText, page: 'home', body: renderHome(language) }));
    const source = await readFile(join(repositoryRoot, `docs/USER_GUIDE.${language}.md`), 'utf8');
    const guide = renderGuide(source, language);
    const label = language === 'zh-TW' ? '本頁目錄' : 'On this page';
    const toc = guide.toc.map(item => `<li class="toc-level-${item.level}"><a href="#${escapeHtml(item.id)}">${escapeHtml(item.label)}</a></li>`).join('');
    const body = `<div class="container guide-shell"><aside class="guide-sidebar"><nav class="guide-toc" aria-label="${label}"><strong>${label}</strong><ol>${toc}</ol></nav></aside><article class="guide-article">${guide.html}</article></div>`;
    await writePage(outputRoot, join(language, 'guide'), renderLayout({ language, title: language === 'zh-TW' ? '完整使用說明' : 'Complete user guide', description: language === 'zh-TW' ? 'OpenCam 完整使用說明' : 'Complete OpenCam user guide', page: 'guide', body }));
  }
  for (const [source, target] of requiredAssets) await copyFile(join(repositoryRoot, source), join(outputRoot, 'assets', target));
  for (const name of ['site.css', 'site.js']) await copyFile(join(websiteRoot, 'src', name), join(outputRoot, 'assets', name));
  await validateOutput(outputRoot);
  return outputRoot;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await buildSite();
}
