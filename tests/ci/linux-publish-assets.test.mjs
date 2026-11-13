// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

test('Linux packaging consumes the publish-time assets snapshot before probe restores can replace it',async()=>{
  const workflow=await readFile(new URL('../../.github/workflows/linux-app-test.yml',import.meta.url),'utf8');
  const publish=workflow.indexOf('dotnet publish src/ScreenRecorder.UI/ScreenRecorder.UI.csproj');
  const snapshot=workflow.match(/cp src\/ScreenRecorder.UI\/obj\/native-only\/project\.assets\.json "([^"]+)"/);
  assert.ok(snapshot,'Preserve actual self-contained publish assets');
  const snapshotAt=workflow.indexOf(snapshot[0]);
  const probes=workflow.indexOf('dotnet publish tests/ScreenRecorder.LinuxAppProbe/');
  assert.ok(publish>=0 && publish<snapshotAt && snapshotAt<probes,'Snapshot must precede subsequent probe restores');
  const command=workflow.split('\n').find(line=>line.includes('bash scripts/linux/package-verified-app.sh'));
  assert.ok(command.endsWith(`"$GITHUB_SHA" "${snapshot[1]}"`),'Package the preserved assets, not mutable obj output');
  const packager=await readFile(new URL('../../scripts/linux/package-verified-app.sh',import.meta.url),'utf8');
  assert.match(packager,/publish_assets=\$6/);
  assert.match(packager,/copy-runtime-notices\.mjs "\$package_directory\/linux-notices" "\$publish_assets"/);
});
