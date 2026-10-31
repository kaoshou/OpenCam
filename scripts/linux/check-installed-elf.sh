#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# CI-only policy: relative installed path, ldd status, report on stdin.
set -euo pipefail
[[ $# == 2 && "$2" =~ ^[0-9]+$ ]] || exit 2
dependencies=$(head -c 1048577)
[[ ${#dependencies} -le 1048576 ]] || exit 2
if [[ "$2" != 0 ]]; then
  printf 'ELF inspection failed: %s (ldd exit=%s)\n%s\n' "$1" "$2" "$dependencies" >&2
  exit 1
fi
missing=$(printf '%s\n' "$dependencies" | grep 'not found' || true)
if [[ -n "$missing" ]]; then
  if [[ "$1" == libcoreclrtraceptprovider.so &&
        "$(printf '%s\n' "$missing" | wc -l | tr -d ' ')" == 1 && "$missing" == *'liblttng-ust.so.0 => not found'* ]]; then
    echo 'Optional LTTng tracing unavailable; no ABI workaround applied.'
  else
    printf 'Missing installed runtime dependencies: %s\n%s\n' "$1" "$missing" >&2
    exit 1
  fi
fi
