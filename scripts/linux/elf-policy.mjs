// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
export function classifyElfDependencies(relativePath, output) {
  const missing = [...output.matchAll(/^\s*(\S+)\s+=>\s+not found\s*$/gm)].map(m => m[1]);
  if (!missing.length) return 'PASS';
  // CoreCLR v8.0.21 pal/src/misc/tracepointprovider.cpp explicitly permits
  // dlopen failure for this tracing-only provider. Ubuntu 24 ships a newer ABI.
  // Never exempt another library, or a required dependency of this provider.
  if (relativePath === 'libcoreclrtraceptprovider.so' && missing.length === 1 && missing[0] === 'liblttng-ust.so.0')
    return 'OPTIONAL_LTTNG_UNAVAILABLE';
  throw new Error('Missing required ELF dependencies: ' + missing.join(', '));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { const data = readFileSync(0, 'utf8'); if (data.length > 1024*1024) throw Error('Oversized dependency report');
    console.log(classifyElfDependencies(process.argv[2], data));
  } catch (e) { console.error(e.message); process.exitCode = 1; }
}
