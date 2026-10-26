// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {assertImmutableAsset,releaseApiPath} from '../../scripts/upload-release-asset.mjs';
test('resuming release publication skips only exact existing bytes and never replaces an asset',()=>{
  assertImmutableAsset('a'.repeat(64),'sha256:'+'a'.repeat(64));
  assert.throws(()=>assertImmutableAsset('a'.repeat(64),'sha256:'+'b'.repeat(64)),/different/);
  assert.throws(()=>assertImmutableAsset('a'.repeat(64),'unknown'),/digest/);
});
test('draft and published uploads use the resolved numeric release identity within this repository',()=>{
  assert.equal(releaseApiPath('kaoshou/OpenCam','https://api.github.com/repos/kaoshou/OpenCam/releases/423841674'),
    'repos/kaoshou/OpenCam/releases/423841674');
  for(const value of ['https://api.github.com/repos/elsewhere/OpenCam/releases/423841674',
    'https://example.com/repos/kaoshou/OpenCam/releases/423841674',
    'https://api.github.com/repos/kaoshou/OpenCam/releases/tags/v1.0.0',
    'https://api.github.com/repos/kaoshou/OpenCam/releases/423841674?other=1'])
    assert.throws(()=>releaseApiPath('kaoshou/OpenCam',value),/identity/);
});
