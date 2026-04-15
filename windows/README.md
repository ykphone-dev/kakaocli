# KakaoCli.Win

Windows용 독립 `kakaocli` 구현입니다. 이 디렉터리는 macOS Swift 런타임을 공유하지 않고, 현재 Swift CLI를 행동 명세로 삼아 C#으로 별도 구현합니다.

## Layout

- `src/KakaoCli.Win` - 메인 CLI
- `src/KakaoCli.Win.Core` - 계약 모델, JSON/시간 유틸리티
- `src/KakaoCli.Win.Data` - Windows 데이터 경로/fixture/probe 읽기
- `src/KakaoCli.Win.Automation` - Windows 자동화 및 probe 수집
- `src/KakaoCli.Win.Probe` - probe sprint 실행기

## Goals

- 명령 이름과 핵심 플래그를 현재 Swift 버전과 맞춥니다.
- JSON 필드는 `.omx/fixtures/windows-parity/contract-manifest.md` 기준으로 관리합니다.
- 구현 독립성보다 계약 일치를 우선합니다.

## Current Status

- 솔루션/프로젝트 스캐폴드 완료
- parity contract/fixture 초기화 완료
- Windows probe 수집기 골격 완료
- 실제 Windows KakaoTalk 연동은 아직 probe artifact 수집 전 단계

## Expected Commands

- `status`
- `auth`
- `chats`
- `messages`
- `search`
- `schema`
- `query`
- `send`
- `sync --follow`
- `login`
- `harvest`
- `probe`
- `win-read-chat`

## Build

Windows에서 .NET SDK 설치 후:

```powershell
cd windows
dotnet build KakaoCli.Win.sln
dotnet run --project src/KakaoCli.Win -- --help
dotnet run --project src/KakaoCli.Win.Probe
```

## Experimental Single Chat Reader

`win-read-chat` is a narrow MVP for one Windows KakaoTalk `chatLogs_<chatId>.edb`
file. It does not discover every chat, send messages, or claim full Windows parity.

Requirements:

- .NET 8 SDK/runtime
- `sqlite3.exe` in `PATH`, or pass `--sqlite PATH`
- a known Windows KakaoTalk `pragma`
- the KakaoTalk user id used by the local PC database
- a numeric `chatId`

Examples:

```powershell
# Decrypt only, useful for validating key material.
dotnet run --project src/KakaoCli.Win -- win-read-chat `
  --chat-id 123456789 `
  --pragma "<pragma>" `
  --user-id "<userId>" `
  --decrypt-only

# Read messages after a known per-chat cursor.
dotnet run --project src/KakaoCli.Win -- win-read-chat `
  --chat-id 123456789 `
  --pragma "<pragma>" `
  --user-id "<userId>" `
  --since-log-id 1000 `
  --limit 20

# Follow new messages. Without --since-log-id, it seeds from the current max id.
dotnet run --project src/KakaoCli.Win -- win-read-chat `
  --chat-id 123456789 `
  --pragma "<pragma>" `
  --user-id "<userId>" `
  --follow
```

Options:

- `--edb PATH` - read an explicit `chatLogs_<chatId>.edb`
- `--user-dir PATH` - search under a specific KakaoTalk user directory
- `--sqlite PATH` - path to `sqlite3.exe`
- `--decrypt-output PATH` - stable decrypted SQLite output path for inspection
- `--user-id-start N --user-id-end N` - find a zero-padded user id by decrypting
  only the SQLite header; use a narrow range when possible

Discovery helpers:

```powershell
# Show device identity inputs, generated pragma, memory pragma candidates,
# and likely KakaoTalk user ids observed in KakaoTalk.exe memory.
dotnet run --project src/KakaoCli.Win -- win-identity

# List chatLogs_<chatId>.edb files by newest activity. WAL timestamps are used
# so recently updated chats rise to the top even before SQLite checkpoints.
dotnet run --project src/KakaoCli.Win -- win-list-chatlogs --limit 20 --json

# Explain exactly which pragma/userId/header combinations were attempted.
dotnet run --project src/KakaoCli.Win -- win-diagnose-key --chat-id 123456789

# Read plaintext message records observed in KakaoTalk.exe memory for one chat.
dotnet run --project src/KakaoCli.Win -- win-memory-read-chat --chat-id 123456789

# Follow only newly observed memory records. Without --since-log-id, this seeds
# from the current max in memory and then emits NDJSON for later messages.
dotnet run --project src/KakaoCli.Win -- win-memory-read-chat --chat-id 123456789 --follow

# Forward only new memory-observed messages from one chat to Slack. First run
# seeds the cursor, so old messages are not sent. Image messages with Kakao CDN
# URLs in memory are forwarded as Slack image blocks.
dotnet run --project src/KakaoCli.Win -- win-memory-slack-monitor --chat-id 123456789 --label "Kakao self chat"
```

This command tries two key-material variants (`pragma+userId` repeated to 512
bytes first, then direct concat) and verifies success by checking the decrypted
SQLite header. It currently reads the main `.edb` file only; if KakaoTalk keeps
fresh rows only in a large encrypted WAL sidecar, follow mode may lag until the
main file is checkpointed.

Version note: KakaoTalk for Windows 25.7.3 and newer changelogs mention stronger
message encryption. On KakaoTalk 26.3.1.5062, the legacy public `chatLogs` key
derivation does not decrypt the SQLite header even when pragma and likely user id
candidates are found. In that case `win-diagnose-key` reports `any_match=false`;
use `win-memory-read-chat` as the MVP live-process capture path while the newer
page codec is investigated.

## Probe Artifacts

Probe 결과는 repo 루트의 `.omx/probes/windows-standalone/` 아래에 저장하도록 설계했습니다.
