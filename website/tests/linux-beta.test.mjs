// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {renderHome} from '../src/content.mjs';
import {mkdtemp,mkdir,copyFile,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';

test('Ubuntu Beta guidance stays below hero downloads without a separate promotional section', () => {
  for(const lang of ['zh-TW','en-US']) {
    const html=renderHome(lang);
    assert.match(html,/id="linux-beta"/);
    const hero=html.split('</section>')[0];
    const notes=hero.match(/<div class="download-notes">([\s\S]*?)<\/div>/)?.[1];
    assert.ok(notes);
    assert.match(notes,/Ubuntu 24\.04/);
    assert.match(notes,/Beta/);
    assert.doesNotMatch(html,/<section[^>]*id="linux-beta"/);
    assert.doesNotMatch(html,/<h2>Ubuntu 也能使用 OpenCam|<h2>OpenCam for Ubuntu/);
    assert.match(html,/Ubuntu 24\.04/);
    assert.match(html,/25\.04/);
    assert.match(html,/X11/);
    assert.match(html,/Wayland/);
    assert.match(html,/CPU/);
  }
});

test('verified Linux download flag exposes only published versioned Beta filenames', async t => {
  const root=await mkdtemp(join(tmpdir(),'opencam-linux-downloads-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  await mkdir(join(root,'website/src'),{recursive:true});
  await mkdir(join(root,'docs/images'),{recursive:true});
  for(const name of ['content.mjs','onboarding.mjs','layout.mjs','paths.mjs'])
    await copyFile(new URL('../src/'+name,import.meta.url),join(root,'website/src',name));
  await writeFile(join(root,'VERSION'),'9.9.9\n');
  await writeFile(join(root,'website/downloads.json'),JSON.stringify({version:'1.2.1',linuxBetaVersion:'1.2.1'}));
  await writeFile(join(root,'docs/images/metadata.json'),'{"version":"1.2.0"}');
  const site=await import(pathToFileURL(join(root,'website/src/content.mjs')).href);
  for(const lang of ['zh-TW','en-US']) {
    const html=site.renderHome(lang);
    assert.match(html,/href="https:\/\/github.com\/kaoshou\/OpenCam\/releases\/download\/v1\.2\.1\/OpenCam_v1\.2\.1_Linux_x64_Beta\.deb"/);
    assert.match(html,/OpenCam_v1\.2\.1_Linux_x64_Beta\.tar\.gz/);
    assert.doesNotMatch(html,/releases\/download\/v9\.9\.9/);
  }
  await writeFile(join(root,'website/downloads.json'),JSON.stringify({version:'1.2.1'}));
  const unpublished=await import(pathToFileURL(join(root,'website/src/content.mjs')).href+'?without-linux');
  for(const lang of ['zh-TW','en-US']) {
    const html=unpublished.renderHome(lang);
    assert.doesNotMatch(html,/href="[^"]+_Linux_[^"]+"/);
    assert.match(html,/OpenCam_v1\.2\.1_Windows_x64_Portable\.zip/);
    assert.match(html,/OpenCam_v1\.2\.1_macOS_arm64\.dmg/);
  }
});
