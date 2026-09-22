// SPDX-License-Identifier: AGPL-3.0-or-later
export const BASE_PATH = '/OpenCam/';
const languages = new Set(['zh-TW', 'en-US']);
const pages = new Set(['home', 'guide']);

export function routeFor(language, page) {
  if (!languages.has(language) || !pages.has(page)) throw new TypeError('Unsupported site route');
  return `${BASE_PATH}${language}/${page === 'guide' ? 'guide/' : ''}`;
}

export function assetPath(name) {
  if (typeof name !== 'string' || !name || name.startsWith('/') || name.includes('..') || name.includes('\\') || name.includes('?') || name.includes('#')) {
    throw new TypeError('Unsafe asset name');
  }
  return `${BASE_PATH}assets/${name}`;
}
