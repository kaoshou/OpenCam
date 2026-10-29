// SPDX-License-Identifier: AGPL-3.0-or-later
import MarkdownIt from 'markdown-it';
import { routeFor, assetPath } from './paths.mjs';

const markdown = new MarkdownIt({ html: false, linkify: true, typographer: false });

export function rewriteGuideHref(href, language) {
  if (/^images\/preview_(main|settings|editor|subtitles|audio)_(zhtw|enus)\.png$/.test(href)) return assetPath(href.slice('images/'.length));
  if (href === '../README.md') return 'https://github.com/kaoshou/OpenCam#readme';
  if (href === '../LICENSE') return 'https://github.com/kaoshou/OpenCam/blob/master/LICENSE';
  if (href === 'USER_GUIDE.zh-TW.md') return routeFor('zh-TW', 'guide');
  if (href === 'USER_GUIDE.en-US.md') return routeFor('en-US', 'guide');
  if (/^(https?:|#)/i.test(href)) return href;
  throw new Error(`Unresolved guide link in ${language}: ${href}`);
}

function plainText(inline) {
  return (inline.children || []).map(token => token.content || '').join('');
}

function slugFor(text) {
  return text.normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '') || 'section';
}

export function renderGuide(source, language) {
  if (!['zh-TW', 'en-US'].includes(language)) throw new TypeError('Unsupported site language');
  const tokens = markdown.parse(source, {});
  const counts = new Map();
  const toc = [];
  for (let index = 0; index < tokens.length; index++) {
    const token = tokens[index];
    if (token.type === 'heading_open') {
      const label = plainText(tokens[index + 1]);
      const base = slugFor(label);
      const count = (counts.get(base) || 0) + 1;
      counts.set(base, count);
      const id = count === 1 ? base : `${base}-${count}`;
      token.attrSet('id', id);
      const level = Number(token.tag.slice(1));
      if (level === 2 || level === 3) toc.push({ level, id, label });
    }
    if (token.type === 'inline') {
      let insideLink = false;
      const children = [];
      for (const child of token.children || []) {
        if (child.type === 'link_open') {
          const original = child.attrGet('href');
          child.attrSet('href', rewriteGuideHref(original, language));
          if (/^images\/preview_(main|settings|editor|subtitles|audio)_(zhtw|enus)\.png$/.test(original))
            child.attrSet('data-lightbox', '');
          insideLink = true;
        }
        if (child.type === 'image') {
          const source = child.attrGet('src');
          if (!/^images\/preview_(main|settings|editor|subtitles|audio)_(zhtw|enus)\.png$/.test(source)) {
            throw new Error(`Unexpected local image in ${language} guide`);
          }
          child.attrSet('src', rewriteGuideHref(source, language));
          child.attrSet('loading', 'lazy');
          // Reserve the real preview dimensions before lazy loading, so image
          // links remain useful keyboard/touch targets and do not shift content.
          const kind=source.match(/preview_(main|settings|editor|subtitles|audio)_/)[1];
          const [width,height]={main:[1000,1000],settings:[650,820],editor:[1440,900],subtitles:[780,620],audio:[740,610]}[kind];
          child.attrSet('width',String(width));child.attrSet('height',String(height));
          if (!insideLink) {
            const open = new child.constructor('link_open', 'a', 1);
            open.attrSet('href', child.attrGet('src'));
            open.attrSet('data-lightbox', '');
            open.attrSet('class', 'screenshot-link');
            children.push(open, child, new child.constructor('link_close', 'a', -1));
            continue;
          }
        }
        children.push(child);
        if (child.type === 'link_close') insideLink = false;
      }
      token.children = children;
    }
  }
  return { html: markdown.renderer.render(tokens, markdown.options, {}), toc };
}
