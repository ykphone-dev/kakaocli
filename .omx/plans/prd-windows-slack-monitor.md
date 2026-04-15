# PRD: Windows Allowlisted KakaoTalk Slack Monitor

## Requirements Summary

Build the MVP in the existing `kakaocli` repo, scoped strictly to the existing Windows C# lane under `windows/`.

The MVP is a new Windows-only `slack-monitor` command that detects new messages only in explicitly configured KakaoTalk chat IDs and forwards a safe preview plus metadata to a Slack DM or dry-run sink.

The central safety requirement is stronger than ordinary filtering: unconfigured chats must never be read, materialized, logged, forwarded, or used as diagnostics. Allowlist validation must happen before any message reader or data source is constructed.

## Decision

Use this repo. Do not create a new repo for the MVP.

Use a new narrow Windows runtime boundary instead of reusing the current broad CLI paths. The implementation must not use existing `messages`, `search`, `sync`, `query`, broad fixture loaders, raw SQL command surfaces, or the current untracked `server/index.js` wrapper as the internal runtime source.

## Evidence

- `windows/` already contains a standalone C# lane and project layout.
- `windows/src/KakaoCli.Win/Program.cs` is currently fixture-backed for messages/sync and does not provide live allowlisted reading.
- `windows/src/KakaoCli.Win.Automation/WindowsKakaoAutomation.cs` is currently a placeholder.
- `windows/src/KakaoCli.Win.Automation/WindowsProbeCollector.cs` documents that DB/UI feasibility is still unverified.
- `.omx/fixtures/windows-parity/contract-manifest.md` already defines Windows parity contracts.
- `server/index.js` filters after a generic event stream and defaults open when no subscription exists, so it is unsafe as-is for this MVP.
- `Sources/KakaoCore/Database/DatabaseReader.swift` and `Sources/KakaoCore/Sync/DatabaseWatcher.swift` use broad macOS sync patterns and are references only, not safe templates for this requirement.

## In Scope

- Windows-only `slack-monitor` command.
- Numeric `chat_id` allowlist with human-readable labels.
- Per-chat cursor tracking.
- Allowlisted new text message polling.
- Slack DM delivery and dry-run delivery.
- Safe preview/truncation policy.
- Minimal diagnostics that never contain raw unconfigured message content.
- Unit/integration/privacy tests proving fail-closed behavior.

## Out Of Scope

- KakaoTalk replies.
- OCR parsing.
- Image/file/sticker extraction.
- Raw message/image DB persistence.
- Name-based runtime chat selection.
- Full Windows parity claim.
- Promoting `server/index.js` unchanged.
- Using broad `messages`, `search`, `sync`, `query`, raw SQL, or fixture loaders in the Slack monitor runtime.

## User-Visible Command

```powershell
dotnet run --project windows/src/KakaoCli.Win -- slack-monitor --config path\allowlist.json --dry-run --once
dotnet run --project windows/src/KakaoCli.Win -- slack-monitor --config path\allowlist.json
```

Supported flags:

- `--config <path>`: required JSON config path.
- `--dry-run`: compose/log safe payloads without sending Slack messages.
- `--once`: run one polling pass and exit.
- `--interval-seconds N`: polling interval override.

## Config Contract

Example:

```json
{
  "allowlist": [
    { "chat_id": 123456789, "label": "policy-room" }
  ],
  "slack": {
    "webhook_url_env": "KAKAOCLI_SLACK_WEBHOOK_URL"
  },
  "cursor_path": ".kakaocli/windows-slack-cursors.json",
  "poll_interval_seconds": 2,
  "max_text_chars": 160
}
```

Validation rules:

- `allowlist` must contain at least one entry.
- Every entry must have numeric `chat_id`.
- Every entry must have nonblank `label`.
- Duplicate `chat_id` values are invalid.
- Nonnumeric IDs, name-only entries, empty allowlists, unknown permissive forms, and malformed JSON fail closed.
- Invalid config exits nonzero before any reader/data source is constructed.

## Architecture

New runtime boundary: `slack-monitor` only.

Suggested files/modules:

- `windows/src/KakaoCli.Win.Core/Contracts/AllowlistConfig.cs`
  - JSON model and validator.
  - Performs all fail-closed validation before any reader construction.
- `windows/src/KakaoCli.Win.Data/IAllowlistedMessageReader.cs`
  - Data boundary interface visible to the monitor.
  - Accepts only validated allowlisted chat IDs.
  - Suggested methods:
    - `GetCurrentMaxLogIdsByChatAsync(IReadOnlySet<long> allowedChatIds)`
    - `ReadNewMessagesAsync(IReadOnlyDictionary<long, long> perChatCursor)`
- `windows/src/KakaoCli.Win.Data/AllowlistedMessageReader.cs`
  - Live Windows KakaoTalk data adapter.
  - Must constrain by chat ID and per-chat cursor before projecting sender/timestamp/text fields.
- `windows/src/KakaoCli.Win.Data/MonitorCursorStore.cs`
  - Per-chat cursor JSON store.
  - Prunes unknown/non-allowlisted cursor IDs on load and on save.
- `windows/src/KakaoCli.Win.Data/PollingService.cs`
  - First-run seed, polling loop, cursor advancement, and sink dispatch.
  - Cursor should advance only after sink success or explicit recorded skip decision.
- `windows/src/KakaoCli.Win.Core/Contracts/IMessageSink.cs`
  - Sink interface for Slack and dry-run.
- `windows/src/KakaoCli.Win.Data/SlackMessageSink.cs`
  - Slack DM adapter.
  - Should use Slack's standard message posting API at implementation time.
- `windows/src/KakaoCli.Win.Data/DryRunMessageSink.cs`
  - Test/dry-run sink that emits safe payloads without sending.
- `windows/src/KakaoCli.Win/Program.cs`
  - Adds and routes `slack-monitor`.
  - Build order must be: parse args -> load/validate config -> construct reader with validated config -> load cursor store -> construct sink -> start poller.

Hard runtime boundary:

- `slack-monitor` must not call existing `HandleMessages`, `HandleSearch`, `HandleSync`, `HandleQuery`, or `ProbeArtifactStore.Load*Fixture()` broad loaders.
- `slack-monitor` must not expose or depend on raw SQL APIs.
- Any future setup/enrollment tool must be separate from runtime monitoring.

## Query Boundary

The implementation must prove a per-chat cursor predicate, not a loose global cursor.

Acceptable logical shape:

```sql
WHERE
  (chatId = @chatA AND logId > @cursorA)
  OR (chatId = @chatB AND logId > @cursorB)
```

or an equivalent join against validated allowlist/cursor parameters.

Not acceptable:

```sql
WHERE chatId IN (@allowlist) AND logId > @minimumCursor
```

unless all allowlisted chats intentionally share the same cursor and duplicate/skip behavior is proven, which is not the MVP plan.

Text/sender/timestamp projection may happen only after the allowlist and cursor predicate is applied.

## Cursor Contract

Cursor store example:

```json
{
  "version": 1,
  "chats": {
    "12345": {
      "last_log_id": 987654321,
      "label": "policy-room",
      "updated_at": "2026-04-15T00:00:00Z"
    }
  }
}
```

Rules:

- Cursor keys are stringified chat IDs.
- Cursor file stores only allowlisted chat IDs.
- Unknown/non-allowlisted cursor IDs are ignored and pruned on load.
- First-run seed queries current max log ID only for allowlisted chats, so historical messages are not forwarded by default.
- Cursor advances only after sink success or explicit logged skip decision.

## Slack Payload

Payload should include:

- configured chat label
- chat ID
- observed time and/or message timestamp
- sender metadata if available
- bounded safe text preview
- trigger/cursor status metadata

Payload should not include:

- raw full message body
- image bytes or raw attachment data
- OCR output
- unconfigured chat content

Implementation should consult official Slack docs at implementation time. Current relevant official docs:

- `chat.postMessage`: https://docs.slack.dev/reference/methods/chat.postMessage
- `chat:write`: https://docs.slack.dev/reference/scopes/chat.write

## Acceptance Criteria

1. Given no allowlist, `slack-monitor` exits nonzero before reader construction and performs zero message-content reads.
2. Given malformed, name-only, nonnumeric, duplicate, or blank-label allowlist entries, `slack-monitor` exits nonzero before reader construction.
3. Given one configured `chat_id`, only messages matching that chat ID and its per-chat cursor are eligible for projection.
4. Given a message in an unconfigured chat, no text is read, materialized, processed, logged, cursor-written, or forwarded.
5. Given a new message in a configured chat, one Slack DM or dry-run payload is produced.
6. Given Slack delivery failure, the failure is logged without raw confidential message storage.
7. Given restart, cursor handling prevents duplicate floods and does not silently skip already-unforwarded allowlisted messages.
8. `slack-monitor` does not call `HandleMessages`, `HandleSearch`, `HandleSync`, `HandleQuery`, or `ProbeArtifactStore.Load*Fixture()` at runtime.
9. No KakaoTalk reply path is invoked or implemented for this MVP.
10. No raw message text, raw images, OCR output, or broad query result is persisted to a DB.
11. OCR is not invoked.
12. No Windows parity claim is made unless the separate parity signoff is completed.

## Implementation Steps

1. Add config model and fail-closed validator in `AllowlistConfig.cs`.
2. Add `IAllowlistedMessageReader` and fake test reader.
3. Add `MonitorCursorStore` with atomic-ish write strategy and allowlist pruning on load.
4. Add `PollingService` with first-run seed and per-chat cursor polling.
5. Add `IMessageSink`, `DryRunMessageSink`, and `SlackMessageSink`.
6. Add `slack-monitor` routing in `Program.cs`.
7. Add SDK-only verification harness or extend existing scripts without adding test dependencies.
8. Run fixture/build verification and document the live Windows gate.

## Risks And Mitigations

- Risk: Windows KakaoTalk only permits broad/global reads.
  - Mitigation: stop at feasibility gate and redesign; do not ship Slack forwarding.
- Risk: developers reuse existing broad `sync` or `server` patterns.
  - Mitigation: tests assert `slack-monitor` does not call broad handlers/loaders.
- Risk: Slack preview leaks sensitive data.
  - Mitigation: bounded preview, dry-run first, no raw logs.
- Risk: cursor bug duplicates or skips messages.
  - Mitigation: per-chat cursor tests and sink-success cursor advancement rule.
- Risk: repo contains untracked experimental `server/`.
  - Mitigation: do not promote unchanged; revisit after safe reader exists.

## Follow-Up Staffing

For `$ralph`:

- sequential path is acceptable after feasibility is decided.
- prioritize config/contract/tests before Slack delivery.

For `$team`:

- `architect`: validate live Windows data-source feasibility and the stop/go gate.
- `executor`: implement C# modules and CLI route.
- `test-engineer`: build SDK-only privacy/contract test harness.
- `security-reviewer`: review fail-closed boundaries and logging.
- `verifier`: run build, fixture verification, negative tests, and live gate checklist.

Available agent types:

- `architect`, `executor`, `test-engineer`, `security-reviewer`, `verifier`, `debugger`, `dependency-expert`, `writer`.

