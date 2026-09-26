#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';

const blockingSeverities = new Set(['high', 'critical']);

export function collectVulnerabilities(value, packageContext = {}) {
  if (Array.isArray(value)) {
    return value.flatMap((entry) => collectVulnerabilities(entry, packageContext));
  }
  if (!value || typeof value !== 'object') {
    return [];
  }

  const context = {
    id: value.id ?? value.name ?? packageContext.id ?? 'unknown-package',
    version: value.resolvedVersion ?? value.version ?? packageContext.version ?? 'unknown-version',
  };
  const own = Array.isArray(value.vulnerabilities)
    ? value.vulnerabilities.map((vulnerability) => ({
        package: context.id,
        version: context.version,
        severity: String(vulnerability.severity ?? 'unknown'),
        advisory: String(
          vulnerability.advisoryurl ??
          vulnerability.advisoryUrl ??
          vulnerability.url ??
          'unknown-advisory'),
      }))
    : [];

  return own.concat(
    Object.entries(value)
      .filter(([key]) => key !== 'vulnerabilities')
      .flatMap(([, entry]) => collectVulnerabilities(entry, context)),
  );
}

export function hasBlockingVulnerability(vulnerabilities) {
  return vulnerabilities.some((entry) =>
    blockingSeverities.has(entry.severity.toLowerCase()));
}

function runSelfTest() {
  const fixture = {
    projects: [{
      frameworks: [{
        topLevelPackages: [{
          id: 'Safe.Package',
          resolvedVersion: '1.2.3',
          vulnerabilities: [{ severity: 'Low', advisoryurl: 'https://example.test/low' }],
        }],
        transitivePackages: [{
          id: 'Blocked.Package',
          resolvedVersion: '4.5.6',
          vulnerabilities: [{ severity: 'High', advisoryurl: 'https://example.test/high' }],
        }],
      }],
    }],
  };
  const vulnerabilities = collectVulnerabilities(fixture);
  assert.deepEqual(vulnerabilities, [
    {
      package: 'Safe.Package',
      version: '1.2.3',
      severity: 'Low',
      advisory: 'https://example.test/low',
    },
    {
      package: 'Blocked.Package',
      version: '4.5.6',
      severity: 'High',
      advisory: 'https://example.test/high',
    },
  ]);
  assert.equal(hasBlockingVulnerability(vulnerabilities), true);
  assert.equal(hasBlockingVulnerability(vulnerabilities.slice(0, 1)), false);
  console.log('NuGet vulnerability parser self-test passed.');
}

if (process.argv.includes('--self-test')) {
  runSelfTest();
  process.exit(0);
}

const target = process.argv[2] ?? 'ScreenRecorder.sln';
const result = spawnSync(
  'dotnet',
  ['list', target, 'package', '--vulnerable', '--include-transitive', '--format', 'json'],
  { encoding: 'utf8' },
);
if (result.error) {
  console.error(`Unable to run NuGet vulnerability audit: ${result.error.message}`);
  process.exit(1);
}
if (result.status !== 0) {
  process.stdout.write(result.stdout ?? '');
  process.stderr.write(result.stderr ?? '');
  process.exit(result.status ?? 1);
}

const output = result.stdout ?? '';
const jsonStart = output.indexOf('{');
if (jsonStart < 0) {
  console.error('NuGet vulnerability audit returned no JSON document.');
  process.exit(1);
}

let report;
try {
  report = JSON.parse(output.slice(jsonStart));
} catch (error) {
  console.error(`Unable to parse NuGet vulnerability audit JSON: ${error.message}`);
  process.exit(1);
}

const vulnerabilities = collectVulnerabilities(report);
if (vulnerabilities.length === 0) {
  console.log(`No known vulnerable NuGet packages found in ${target}.`);
  process.exit(0);
}

for (const entry of vulnerabilities) {
  console.log(`${entry.severity}: ${entry.package} ${entry.version} — ${entry.advisory}`);
}
if (hasBlockingVulnerability(vulnerabilities)) {
  console.error('High or Critical NuGet vulnerabilities block packaging.');
  process.exit(1);
}

console.log('No High or Critical NuGet vulnerabilities found.');
