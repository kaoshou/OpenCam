// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {assertImmutableAsset} from '../../scripts/upload-release-asset.mjs';
test('resuming release publication skips only exact existing bytes and never replaces an asset',()=>{
  assertImmutableAsset('a'.repeat(64),'sha256:'+'a'.repeat(64));
  assert.throws(()=>assertImmutableAsset('a'.repeat(64),'sha256:'+'b'.repeat(64)),/different/);
  assert.throws(()=>assertImmutableAsset('a'.repeat(64),'unknown'),/digest/);
});
