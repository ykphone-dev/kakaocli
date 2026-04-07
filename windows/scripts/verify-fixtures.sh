#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-$ROOT_DIR/.omx/tools/dotnet/dotnet}"

if [[ ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet not found at $DOTNET_BIN" >&2
  exit 1
fi

cd "$ROOT_DIR"

"$DOTNET_BIN" build windows/KakaoCli.Win.sln >/tmp/kakaocli-win-build.log

CHAT_JSON="$(mktemp)"
MESSAGE_JSON="$(mktemp)"
SEARCH_JSON="$(mktemp)"
SYNC_JSON="$(mktemp)"
STATUS_JSON="$(mktemp)"

"$DOTNET_BIN" run --no-build --project windows/src/KakaoCli.Win -- chats --json >"$CHAT_JSON"
"$DOTNET_BIN" run --no-build --project windows/src/KakaoCli.Win -- messages --json >"$MESSAGE_JSON"
"$DOTNET_BIN" run --no-build --project windows/src/KakaoCli.Win -- search lunch --json >"$SEARCH_JSON"
"$DOTNET_BIN" run --no-build --project windows/src/KakaoCli.Win -- sync >"$SYNC_JSON"
"$DOTNET_BIN" run --no-build --project windows/src/KakaoCli.Win -- status >"$STATUS_JSON"

python3 - <<'PY' "$ROOT_DIR" "$CHAT_JSON" "$MESSAGE_JSON" "$SEARCH_JSON" "$SYNC_JSON" "$STATUS_JSON"
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
chat_json = pathlib.Path(sys.argv[2])
message_json = pathlib.Path(sys.argv[3])
search_json = pathlib.Path(sys.argv[4])
sync_json = pathlib.Path(sys.argv[5])
status_json = pathlib.Path(sys.argv[6])

fixtures = root / ".omx" / "fixtures" / "windows-parity"

def load(path):
    return json.loads(path.read_text())

actual_chats = load(chat_json)
actual_messages = load(message_json)
actual_search = load(search_json)
actual_sync = load(sync_json)
actual_status = load(status_json)

expected_chats = load(fixtures / "chats.json")
expected_messages = load(fixtures / "messages.json")
expected_search = load(fixtures / "search.json")

assert actual_chats == expected_chats, "chats output mismatch"
assert actual_messages == expected_messages, "messages output mismatch"
assert actual_search == expected_search, "search output mismatch"
assert actual_sync["status"] == "ready", "sync one-shot status mismatch"
assert "InstallPath" in actual_status, "status output missing InstallPath"
assert "Notes" in actual_status, "status output missing Notes"

print("fixture-verification-ok")
PY

echo "verify-fixtures: PASS"
