# Test Spec: Windows Allowlisted KakaoTalk Slack Monitor

## Scope

This test spec covers the MVP from `.omx/plans/prd-windows-slack-monitor.md`.

The proof target is not merely "does a Slack message send." The proof target is:

1. invalid or unsafe config opens no reader and reads no message content;
2. configured chats are the only chats whose rows may be projected;
3. unconfigured chats produce no text materialization, logs, cursor writes, or sink calls;
4. Slack delivery/dry-run receives only safe allowlisted payloads.

## Test Constraints

- Avoid new package dependencies unless explicitly approved.
- Prefer a small SDK-only console/privacy test harness or extend existing fixture scripts.
- Continue running `bash windows/scripts/verify-fixtures.sh` for current fixture parity.
- Add explicit `dotnet build windows/KakaoCli.Win.sln` verification.
- Live Windows KakaoTalk testing is a gated manual/integration step, not something to fake as completed.

## Unit Tests

### Config Validation

Cases that must fail before reader construction:

- missing config path
- malformed JSON
- missing `allowlist`
- empty `allowlist`
- missing `chat_id`
- nonnumeric `chat_id`
- duplicate `chat_id`
- missing `label`
- blank `label`
- name-only allowlist entry
- unknown permissive forms like `"all": true`

Required assertion:

- fake reader factory construction count remains `0`.
- process/handler returns nonzero or an explicit invalid-config result.

### Cursor Store

Cases:

- saves per-chat `last_log_id`, `label`, `updated_at`.
- loads only allowlisted chat IDs.
- prunes unknown/non-allowlisted cursor entries on load.
- does not create cursor entries for invalid config.
- cursor advances only after sink success or explicit recorded skip.

### Preview Policy

Cases:

- truncates to `max_text_chars`.
- handles null/empty text.
- does not include raw full body when longer than preview length.
- output is deterministic.

## Contract Tests

### Reader Contract

`IAllowlistedMessageReader` must expose only allowlisted operations:

- `GetCurrentMaxLogIdsByChatAsync(allowedChatIds)`
- `ReadNewMessagesAsync(perChatCursor)`

It must not expose:

- read all chats
- search
- raw SQL
- generic sync
- fixture-load APIs

### Query Predicate Proof

The adapter must build an equivalent of:

```sql
WHERE
  (chatId = @chatA AND logId > @cursorA)
  OR (chatId = @chatB AND logId > @cursorB)
```

before projecting message text/sender/timestamp fields.

Tests must reject a loose global cursor equivalent to:

```sql
WHERE chatId IN (@allowlist) AND logId > @minimumCursor
```

unless the implementation explicitly proves it cannot duplicate/skip per-chat messages; this proof is out of MVP scope.

## Integration Tests With Fake Data Source

### Positive Allowlist Case

Input:

- allowlist: chat `123`, label `policy-room`
- cursor: chat `123` last log `10`
- fake rows:
  - chat `123`, log `11`, text `allowed`

Expected:

- one dry-run or fake Slack payload
- payload includes label, chat ID, timestamp/observed time, sender if available, preview text, cursor metadata
- cursor advances to `11`

### Negative Unconfigured Case

Input:

- allowlist: chat `123`
- cursor: chat `123` last log `10`
- fake rows:
  - chat `999`, log `999`, text `must-not-read`

Expected:

- zero text materialization for chat `999`
- zero dry-run/Slack payloads
- zero cursor entries for chat `999`
- zero raw logs containing `must-not-read`

### Invalid Config Gate

Input:

- name-only or empty allowlist
- fake reader configured to throw if constructed/opened

Expected:

- handler exits invalid-config
- fake reader is never constructed/opened
- zero sink calls

### Duplicate Suppression

Input:

- allowlist chat `123`
- cursor last log `11`
- fake rows include log `11` and `12`

Expected:

- log `11` ignored
- log `12` sent once
- cursor advances to `12`
- rerun sends zero payloads for log `12`

## Static/Boundary Tests

The implementation must include checks or review gates proving:

- `slack-monitor` does not call `HandleMessages`.
- `slack-monitor` does not call `HandleSearch`.
- `slack-monitor` does not call `HandleSync`.
- `slack-monitor` does not call `HandleQuery`.
- `slack-monitor` does not call `ProbeArtifactStore.LoadChatsFixture`.
- `slack-monitor` does not call `ProbeArtifactStore.LoadMessagesFixture`.
- `slack-monitor` does not call `ProbeArtifactStore.LoadSearchFixture`.
- `slack-monitor` does not call `ProbeArtifactStore.LoadSyncFixture`.
- `slack-monitor` does not use the current `server/index.js` as an internal source.

## Negative Gates

The release must fail if:

- empty allowlist reads any message content.
- name-only allowlist reads any message content.
- unconfigured chat text appears in payload, logs, cursor, or test output.
- any KakaoTalk reply/send path is invoked.
- OCR is invoked.
- raw message text or images are stored in DB.
- broad/global source is used before allowlist predicate.
- current `server/index.js` is promoted unchanged.

## Live Windows Gate

Run only on a controlled Windows KakaoTalk installation.

Setup:

- configure one controlled allowlisted chat ID.
- configure one unconfigured chat for negative observation.
- use dry-run first.

Steps:

1. Run `slack-monitor --config <path> --dry-run --once` and verify first-run seed does not forward history.
2. Send one new message to the allowlisted chat.
3. Run or continue monitor and verify one dry-run payload.
4. Send/stage one new message in an unconfigured chat.
5. Verify no output, no cursor entry, no raw text in logs.
6. Enable Slack sink only after dry-run passes.
7. Verify one Slack DM for the allowlisted message.

Stop condition:

- If live Windows access cannot filter by chat ID and cursor before content projection, stop implementation and return to architecture.

## Verification Commands

Existing baseline:

```bash
bash windows/scripts/verify-fixtures.sh
```

Build:

```bash
.omx/tools/dotnet/dotnet build windows/KakaoCli.Win.sln
```

If using system dotnet on Windows:

```powershell
dotnet build windows/KakaoCli.Win.sln
```

