#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-$ROOT_DIR/.omx/tools/dotnet/dotnet}"

if [[ ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet not found at $DOTNET_BIN" >&2
  exit 1
fi

cd "$ROOT_DIR"

"$DOTNET_BIN" build windows/KakaoCli.Win.sln >/tmp/kakaocli-win-slack-monitor-build.log
"$DOTNET_BIN" run --project windows/src/KakaoCli.Win.Verify

echo "verify-slack-monitor: PASS"
