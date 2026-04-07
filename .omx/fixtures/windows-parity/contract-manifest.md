# Windows Parity Contract Manifest

This manifest defines what the standalone Windows CLI must preserve from the current Swift `kakaocli`.

## Commands

| Command | Classification | Notes |
| --- | --- | --- |
| `status` | `must-match` | Same intent; text output may differ operationally but core sections must exist. |
| `auth` | `must-match` | Same goal: prove local data access / decryption path. |
| `chats` | `must-match` | Preserve `--limit`, `--json`, and JSON field names. |
| `messages` | `must-match` | Preserve filters and JSON field names. |
| `search` | `must-match` | Preserve query argument, `--limit`, `--json`, and JSON field names. |
| `schema` | `must-match` | Preserve schema dump intent. |
| `query` | `must-match` | Preserve raw read-only query intent. |
| `sync --follow` | `must-match` | Preserve NDJSON event field names and one-shot readiness mode. |
| `login` | `must-match` | Preserve login-management intent; backend mechanics may differ. |
| `send` | `must-match` | Preserve self-chat and named-chat intent. |
| `harvest` | `must-match` | Preserve names/history capture intent; exact internals may differ. |
| `probe` | `windows-only` | New Windows-only command for probe sprint evidence capture. |

## JSON Fields

### `chats --json`

- `id` - `must-match`
- `type` - `must-match`
- `display_name` - `must-match`
- `member_count` - `must-match`
- `unread_count` - `must-match`
- `last_message_at` - `must-match`

### `messages --json`

- `id` - `must-match`
- `chat_id` - `must-match`
- `sender_id` - `must-match`
- `type` - `must-match`
- `timestamp` - `must-match`
- `is_from_me` - `must-match`
- `sender` - `must-match`
- `text` - `must-match`

### `sync --follow`

- `type` - `must-match`
- `log_id` - `must-match`
- `chat_id` - `must-match`
- `chat_name` - `must-match`
- `sender_id` - `must-match`
- `sender` - `must-match`
- `text` - `must-match`
- `message_type` - `must-match`
- `timestamp` - `must-match`
- `is_from_me` - `must-match`

## Operational Differences

- OS-specific install paths are `may-differ`.
- credential storage backend is `may-differ`.
- exact human-readable status lines are `may-differ` if intent and troubleshooting value are preserved.

## Release Rule

Do not claim Windows parity unless every `must-match` item is either verified or explicitly waived in `parity-signoff.md`.
