import { mkdir, readFile, writeFile, copyFile, stat } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderEntry, renderLayout } from './src/layout.mjs';
import { renderHome, content } from './src/content.mjs';

const websiteRoot = fileURLToPath(new URL('.', import.meta.url));
const requiredAssets = [
  ['src/ScreenRecorder.UI/Assets/app_icon.png', 'app_icon.png'],
  ['docs/images/preview_main_zhtw.png', 'preview_main_zhtw.png'],
  ['docs/images/preview_main_enus.png', 'preview_main_enus.png'],
  ['docs/images/preview_settings_zhtw.png', 'preview_settings_zhtw.png'],
  ['docs/images/preview_settings_enus.png', 'preview_settings_enus.png']
];

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
    await writePage(outputRoot, join(language, 'guide'), renderLayout({ language, title: 'Guide', description: 'User guide', page: 'guide', body: '<h1>Guide</h1>' }));
  }
  for (const [source, target] of requiredAssets) await copyFile(join(repositoryRoot, source), join(outputRoot, 'assets', target));
  for (const name of ['site.css', 'site.js']) await copyFile(join(websiteRoot, 'src', name), join(outputRoot, 'assets', name));
  return outputRoot;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await buildSite();
}
