# Windows Standalone KakaoCLI Plan

## Requirements Summary

- Build a standalone Windows KakaoTalk CLI, preferably in C#, that delivers the same user-facing command families as the current macOS tool:
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
- Match the current CLI contract where practical:
  - command names
  - important flags
  - JSON field names
  - exit semantics
- Do not require a shared runtime or shared internal implementation with the existing Swift codebase.
- Keep the same product boundary: local KakaoTalk data access plus desktop automation, not private Kakao APIs.

## RALPLAN-DR Summary

### Principles

1. Preserve the product contract, not the internal runtime.
2. Use Windows-native tooling for Windows-native problems.
3. Prove the hard parts early with targeted probes.
4. Prioritize user-visible parity over code reuse.
5. Keep the Windows implementation operationally independent from the macOS one.

### Decision Drivers

1. The current codebase is deeply macOS-specific:
   - [Package.swift](/Users/kerry/Documents/GitHub/kakaocli/Package.swift#L7)
   - [KakaoCLI.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/KakaoCLI.swift#L3)
   - [KakaoAutomator.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCore/Automation/KakaoAutomator.swift#L11)
   - [LoginAutomator.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCore/Automation/LoginAutomator.swift#L11)
2. The hardest Windows work is OS integration, UI automation, and local-data discovery, which favor C#.
3. Shared runtime complexity is not justified when the user only needs equivalent behavior on Windows.

### Viable Options

#### Option A: Swift cross-platform port

Approach: port the existing Swift code to Windows with conditional implementations.

Pros:
- Highest nominal code reuse.
- One language.

Cons:
- Worst fit for Windows automation.
- Preserves the wrong constraints.
- High risk of fighting tooling instead of shipping functionality.

#### Option B: Hybrid shared-runtime or bridge model

Approach: keep a shared core and attach a Windows backend via bridge/subprocess/runtime integration.

Pros:
- Some shared logic possible.
- One conceptual product tree.

Cons:
- Adds bridge complexity the user does not need.
- Risks inheriting both Swift and C# operational burdens.
- Still may share very little in practice.

#### Option C: Standalone Windows CLI in C# with parity-by-contract

Approach: build a separate Windows implementation in C# that matches the current CLI and JSON contract, using the Swift project as behavioral reference.

Pros:
- Best fit for Windows APIs and UI Automation.
- Simplest implementation story for the platform.
- No artificial shared-runtime constraints.

Cons:
- Highest drift risk over time.
- Requires disciplined contract fixtures and documentation.
- Feature updates may need dual implementation.

### Favored Path

Favor **Option C**.

The most valuable shared asset is the user-facing contract, not the runtime. A standalone Windows CLI avoids unnecessary architecture while maximizing the chance of actually delivering login/send/sync/harvest on Windows.

### Invalidation Rationale

- Reject Option A because the current implementation is strongly shaped by macOS frameworks and assumptions.
- Reject Option B because it preserves complexity without a clear user benefit once shared runtime is explicitly unnecessary.

## Strongest Steelman Counterargument

The strongest argument against a standalone C# CLI is that it may optimize the implementation path while weakening the product guarantee. The current tool’s real value is not just its command names in [KakaoCLI.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/KakaoCLI.swift#L3); it is the accumulated behavior hidden behind those commands: specific JSON fields, error shapes, ordering, query semantics, and edge-case handling. A clean-room Windows implementation risks becoming “similar” rather than “the same,” especially because the macOS project embeds behavior in platform-specific code paths such as [KakaoAutomator.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCore/Automation/KakaoAutomator.swift#L11) and [LoginAutomator.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCore/Automation/LoginAutomator.swift#L11). If parity governance is weak, Option C ships faster but silently degrades the cross-platform product contract over time.

## Meaningful Tradeoff Tension

- **Implementation speed vs contract fidelity**
  - A standalone Windows CLI is the fastest way to use the right OS-native stack.
  - It also has the highest risk of subtle contract drift unless the parity contract is formalized and tested.

- **Windows-native ergonomics vs cross-platform sameness**
  - Native Windows behavior may suggest different flags, defaults, or automation flows.
  - Preserving agent compatibility requires resisting “better Windows UX” changes unless they are deliberately versioned.

## Synthesis Path

Use standalone C# as the runtime strategy, but strengthen parity governance so the Windows tool is independent internally and strict externally.

1. Freeze macOS CLI behavior as the normative contract.
2. Generate golden fixtures and a contract manifest from the existing Swift tool.
3. Build the Windows CLI independently in C#.
4. Gate releases on fixture comparison and smoke evidence, not on implementation similarity.

This keeps the runtime independent while preserving a single product definition.

## Acceptance Criteria

- A Windows executable exists and supports the same command families with equivalent intent.
- JSON outputs for `chats`, `messages`, `search`, and `sync --follow` match the current contract closely enough for existing agent integrations.
- Windows `auth` can prove real access to local KakaoTalk data or the equivalent authoritative data source.
- Windows `sync --follow` emits live new-message events.
- Windows `send` works for self-chat and named chats.
- Windows `login` detects runtime state and performs credentialed login with actionable failure reporting.
- Windows `harvest` either works with evidence or is explicitly bounded as a known gap before release claims are made.
- Contract fixtures and smoke tests exist to keep macOS and Windows behavior aligned.
- A contract classification exists that clearly marks:
  - must-match fields/flags/exit codes
  - may-differ operational details

## Contract Governance Rules

- Store fixture data under `.omx/fixtures/windows-parity/`.
- Store a contract manifest under `.omx/fixtures/windows-parity/contract-manifest.md`.
- Store final release evidence under `.omx/fixtures/windows-parity/parity-signoff.md`.
- Every command field/flag gets one of:
  - `must-match`
  - `may-differ`
  - `windows-only`
- No Windows release may claim parity if any `must-match` item is unverified.
- Any intentional contract difference requires explicit documentation and versioned release notes.

## Implementation Steps

1. Freeze the macOS product contract as the Windows spec.
   - Touchpoints:
     - [KakaoCLI.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/KakaoCLI.swift#L3)
     - [ChatsCommand.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/Commands/ChatsCommand.swift#L1)
     - [MessagesCommand.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/Commands/MessagesCommand.swift#L1)
     - [SearchCommand.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/Commands/SearchCommand.swift#L1)
     - [SyncCommand.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/Commands/SyncCommand.swift#L1)
     - [SendCommand.swift](/Users/kerry/Documents/GitHub/kakaocli/Sources/KakaoCLI/Commands/SendCommand.swift#L1)
   - Capture:
     - command names and flags
     - JSON fixtures
     - error messages and exit behavior
     - ordering and default behaviors that agents might rely on

2. Create a Windows probe sprint in C#.
   - Deliver artifacts for:
     - KakaoTalk install path
     - process and window discovery
     - local data/config path
     - DB file presence and schema
     - decryption/open feasibility
     - UI Automation tree for login, chat list, and chat window
   - Output under `.omx/probes/windows-standalone/`:
     - `environment.md`
     - `paths.json`
     - `db-schema.sql` or `db-schema.md`
     - `uia-login-tree.json`
     - `uia-chat-tree.json`
     - `probe-verdict.md`
   - This stage is a release gate for implementation, not a side task.

3. Scaffold a standalone C# solution.
   - Suggested shape:
     - `windows/src/KakaoCli.Win` for the executable
     - `windows/src/KakaoCli.Win.Core` for domain models and contract helpers
     - `windows/src/KakaoCli.Win.Automation` for UI Automation
     - `windows/src/KakaoCli.Win.Data` for DB/data discovery
     - `windows/tests/KakaoCli.Win.Tests` for contract tests
   - Prefer keeping the Windows project physically separate from the Swift targets.

4. Implement the read stack first.
   - `status`
   - `auth`
   - `chats`
   - `messages`
   - `search`
   - `schema`
   - `query`
   - `sync --follow`
   - Goal: prove Windows local data access and event streaming before UI automation-heavy work.

5. Implement Windows automation next.
   - `login`
   - `send` to self-chat
   - `send` to named chat
   - `harvest` names-only
   - `harvest` history loading
   - Use Windows UI Automation first; use OCR/image fallback only where necessary.
   - Treat self-chat send and login as hard proof gates before claiming parity.

6. Add parity governance and regression gates.
   - Store contract fixtures under `.omx/fixtures/windows-parity/`.
   - Store final parity evidence under `.omx/fixtures/windows-parity/parity-signoff.md`.
   - Compare Windows outputs against frozen macOS fixture shapes.
   - Add a command-by-command release checklist.
   - Reject releases that fail any `must-match` item without an explicit contract decision.

7. Package and document Windows support.
   - Windows setup steps
   - privilege requirements
   - KakaoTalk assumptions
   - supported Windows versions
   - known gaps, if any
   - contract differences, if any

## Risks and Mitigations

- Risk: Windows KakaoTalk data is inaccessible or too different.
  - Mitigation: probe before implementation commitments.

- Risk: login/send/harvest automation is unreliable.
  - Mitigation: treat self-chat send and login as early proof gates.

- Risk: standalone implementation drifts from macOS behavior.
  - Mitigation: freeze fixtures, classify contracts, and make them release gates.

- Risk: `harvest` parity is much harder than other features.
  - Mitigation: stage it last and do not claim parity without evidence.

## Verification Steps

- Capture macOS reference fixtures from the existing CLI.
- Complete Windows probe artifacts.
- Run contract tests against Windows JSON outputs.
- Run manual smoke tests on a real Windows KakaoTalk install:
  - status
  - auth
  - chats
  - messages
  - search
  - sync --follow
  - login
  - self-chat send
  - named chat send
  - harvest names-only
  - harvest history load
- Produce a parity signoff checklist before any “same features on Windows” claim.

## ADR

### Decision

Build a standalone Windows KakaoCLI in C# and govern parity through CLI/JSON contract fixtures rather than a shared runtime.

### Drivers

- Windows-native APIs and automation are better served by C#.
- The user does not need runtime sharing.
- The existing Swift code is a behavioral reference, not a portable foundation.

### Alternatives Considered

- Swift cross-platform port
- hybrid shared-runtime model

### Why Chosen

It is the simplest architecture that still targets full user-visible parity and gives the best chance of shipping hard Windows automation features.

### Consequences

- We will maintain two implementations.
- Contract management and fixture discipline become central.
- macOS and Windows can evolve independently internally as long as the user contract stays aligned.

### Follow-ups

- Decide exact C# CLI stack and packaging.
- Decide fixture generation workflow from the macOS tool.
- Decide whether release branding remains `kakaocli` or uses a Windows-specific package name.

## Available-Agent-Types Roster

- `architect`
- `dependency-expert`
- `executor`
- `debugger`
- `test-engineer`
- `verifier`
- `writer`

## Follow-up Staffing Guidance

### Ralph Path

- `dependency-expert` high
  - Windows KakaoTalk storage/UI Automation feasibility
- `architect` medium
  - solution layout and parity governance
- `executor` high
  - C# implementation
- `test-engineer` medium
  - fixture and smoke-test strategy
- `verifier` high
  - parity evidence

### Team Path

- 1 `dependency-expert`
- 1 `architect`
- 2 `executor`
- 1 `test-engineer`
- 1 `writer`
- closeout by `verifier`

## Launch Hints

- Ralph:
  - `$ralph implement .omx/plans/windows-standalone-kakaocli-consensus-plan.md`
- Team:
  - `$team implement .omx/plans/windows-standalone-kakaocli-consensus-plan.md`
  - `omx team start --plan .omx/plans/windows-standalone-kakaocli-consensus-plan.md`

## Team Verification Path

- Team proves:
  - macOS contract fixtures captured
  - Windows probe sprint complete
  - standalone C# solution scaffolded
  - Windows read path works
  - login and self-chat send work
  - harvest status is proven or honestly bounded
- Ralph verifies after handoff:
  - acceptance criteria evidence is complete
  - docs match actual support
  - parity claims do not exceed the evidence

## Pre-Mortem

### Scenario 1: Windows DB path or decryption is not reproducible

- Symptom: read stack stalls before `auth`.
- Prevention: make probe artifacts a release gate for the implementation phase.

### Scenario 2: UI Automation works for read-adjacent flows but not send/login reliably

- Symptom: flaky login or message delivery.
- Prevention: self-chat send and login become early proof targets, not late cleanup.

### Scenario 3: Standalone CLI drifts from the macOS contract over time

- Symptom: JSON or CLI flags diverge and agent integrations break.
- Prevention: fixture-based parity checks and release checklist.

## Expanded Test Plan

### Unit

- duration parsing
- JSON field naming
- command option parsing
- event serialization

### Integration

- DB discovery/open on Windows
- sync watcher against a live or captured dataset
- UI Automation wrappers against test windows where possible

### E2E

- status
- auth
- chats
- messages
- search
- sync --follow
- login
- self-chat send
- named chat send
- harvest

### Observability

- structured logs for app-state transitions
- structured logs for automation failures
- stored probe artifacts
- command-level evidence bundles for parity signoff

## Applied Improvements Changelog

- Switched the favored architecture from hybrid to standalone Windows C# implementation.
- Added the strongest steelman counterargument against the standalone option.
- Added explicit contract governance with `must-match` / `may-differ` / `windows-only` classification.
- Tightened release gates around parity claims so independence does not become silent drift.
