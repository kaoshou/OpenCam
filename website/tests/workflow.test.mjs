import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

test('Pages workflow builds site separately from desktop releases', async () => {
  const path = resolve(import.meta.dirname, '../../.github/workflows/deploy-pages.yml');
  const yaml = await readFile(path, 'utf8');
  for (const expected of ['branches: [master]', "'website/**'", "'docs/USER_GUIDE.*.md'", "'docs/images/**'", "'src/ScreenRecorder.UI/Assets/app_icon.png'", "'.github/workflows/deploy-pages.yml'", 'workflow_dispatch:', 'npm ci --prefix website', 'npm test --prefix website', 'npm run build --prefix website', 'path: website/dist', 'name: github-pages', 'pages: write', 'id-token: write', "github.ref == 'refs/heads/master'"]) {
    assert.ok(yaml.includes(expected), `Missing ${expected}`);
  }
  assert.doesNotMatch(yaml, /gh release|tags:/);
  assert.match(yaml, /deploy:\s*\n\s*if: github\.ref/);
});
