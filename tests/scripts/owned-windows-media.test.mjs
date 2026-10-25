// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { validateMediaCapabilities } from '../../scripts/check-owned-media-tools.mjs';

const complete={encoders:'libx264 aac pcm_s16le gif png h264_nvenc h264_qsv h264_amf',
  decoders:'h264 hevc vp9 libdav1d aac mp3 png',
  filters:'scale crop pad overlay fps trim setpts concat aresample atrim asetpts amix volume afade fade palettegen paletteuse testsrc sine color aformat anullsrc showwavespic',
  formats:'matroska mov mp4 image2 rawvideo s16le lavfi null',protocols:'file pipe fd'};
test('owned media tools retain required teaching, export, decode and hardware probe capabilities',()=>{
  validateMediaCapabilities(complete,'win-x64');
  for(const group of ['encoders','decoders','filters','formats','protocols'])
    assert.throws(()=>validateMediaCapabilities({...complete,[group]:''},'win-x64'),/Missing/);
  assert.throws(()=>validateMediaCapabilities({...complete,encoders:complete.encoders.replace(' png','')},'win-x64'),/Missing encoders capability: png/);
});
test('release profile uses checksum-pinned source builds rather than third-party binaries',async()=>{
  const workflow=await readFile(new URL('../../.github/workflows/verify-obs-core.yml',import.meta.url),'utf8');
  assert.match(workflow,/build-ffmpeg-windows\.sh/);
  assert.match(workflow,/check-owned-media-tools\.mjs/);
  const script=await readFile(new URL('../../scripts/build-ffmpeg-windows.sh',import.meta.url),'utf8');
  assert.match(script,/sha256sum -c/);
  assert.match(script,/--disable-autodetect/);
  assert.match(script,/--enable-libvpl/);
  assert.match(script,/--enable-zlib/);
  assert.match(script,/-DZLIB_BUILD_SHARED=OFF/);
  assert.match(script,/cp "\$prefix\/lib\/libzs\.a" "\$prefix\/lib\/libz\.a"/);
  assert.match(script,/sources/);
  assert.match(script,/libvpl-mingw-secure-functions\.patch/);
  assert.match(script,/cp config\.h ffbuild\/config\.log ffbuild\/config\.mak/);
  assert.match(script,/pacman -Ql/);
  assert.doesNotMatch(script,/cp -R \/ucrt64\/share\/licenses\/gcc-libs/);
});
