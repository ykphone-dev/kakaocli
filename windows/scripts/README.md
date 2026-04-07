# Windows Verification Scripts

- `verify-fixtures.sh`
  - Builds the Windows C# solution
  - Runs fixture-backed commands
  - Verifies `chats`, `messages`, and `search` against the parity fixtures
  - Verifies `sync` one-shot readiness output
  - Verifies `status` JSON shape
