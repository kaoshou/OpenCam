import MarkdownIt from 'markdown-it';
import { routeFor } from './paths.mjs';

const markdown = new MarkdownIt({ html: false, linkify: true, typographer: false });

export function rewriteGuideHref(href, language) {
  if (href === '../README.md') return 'https://github.com/kaoshou/OpenCam#readme';
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
      for (const child of token.children || []) {
        if (child.type === 'link_open') child.attrSet('href', rewriteGuideHref(child.attrGet('href'), language));
        if (child.type === 'image') throw new Error(`Unexpected local image in ${language} guide`);
      }
    }
  }
  return { html: markdown.renderer.render(tokens, markdown.options, {}), toc };
}
