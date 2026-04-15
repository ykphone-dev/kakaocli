# Consensus Plan: Windows Allowlisted KakaoTalk Slack Monitor

## Status

Consensus approved after Planner -> Architect -> Critic iterations.

- Planner initial recommendation: same repo, Windows lane.
- Architect first verdict: ITERATE; enforce allowlist at data-access boundary.
- Planner revision: fail-closed allowlisted reader design.
- Architect second verdict: APPROVE; add per-chat cursors and enrollment/runtime separation.
- Critic first verdict: ITERATE; add concrete files, config schema, cursor contract, fake reader tests, Slack dry-run contract.
- Planner final revision: added concrete implementation/test boundaries.
- Architect final verdict: APPROVE; emphasize per-chat cursor predicate, not loose `IN + minCursor`.
- Critic final verdict: APPROVE; add explicit file paths, SDK-only test guidance, broad-handler acceptance, and build verification.

## Final Decision

Keep the work in this repo.

Implement the MVP under the existing `windows/` lane as a narrow `slack-monitor` path. Do not create a new repo unless a future organizational policy or Windows feasibility finding requires physical separation.

## Why Not A New Repo

Rejected for MVP:

- `windows/` already exists and is a standalone C# implementation lane.
- `.omx/fixtures/windows-parity/` already gates contracts.
- splitting would weaken safety and contract governance.
- a new repo does not solve the core risk; the core risk is boundary placement before content reads.

## Why Not Existing `server/`

Rejected unchanged:

- it wraps generic `sync --follow`;
- it filters after event JSON has already been parsed/materialized;
- it defaults to all events when no subscriptions are configured;
- it is currently untracked;
- it cannot satisfy "unconfigured chats must never be read" as-is.

## Why Not Generic `sync`

Rejected as internal source:

- existing macOS sync uses global cursor and broad `messagesSince`;
- existing Windows sync is fixture-backed;
- both patterns are unsafe for the Slack monitor runtime.

## RALPLAN-DR

Principles:

- Fail closed before any message reader is constructed.
- Enforce privacy at the data-access boundary.
- Use same-repo Windows governance for contracts and fixtures.
- Keep MVP narrow: allowlisted text new message -> Slack DM or dry-run.
- Do not reuse broad read APIs in the runtime path.

Decision drivers:

- strongest user boundary: unconfigured chats must never be read;
- Windows live access is not proven;
- implementation should preserve existing repo governance without inheriting unsafe broad sync behavior.

Viable options:

1. Same repo, narrow Windows lane: chosen.
2. New repo: rejected unless future policy/feasibility forces separation.
3. Promote `server/index.js`: rejected unchanged.
4. Reuse generic `sync --follow`: rejected as internal source.

## ADR

Decision:

Build the Windows KakaoTalk allowlisted new-message Slack DM MVP in this repo, under `windows/`, through a new `slack-monitor` runtime boundary backed by an allowlisted message reader.

Drivers:

- confidential message data;
- Windows-first target;
- allowlisted Slack DM delivery;
- no broad chat scanning;
- no raw text or image persistence.

Alternatives considered:

- new repo;
- internal `sync --follow`;
- existing `server/index.js`;
- raw SQL/query path;
- name-based runtime allowlist.

Why chosen:

The repo already has a Windows C# lane and parity fixtures. The safety problem is not solved by repo separation; it is solved by preventing data access until a validated numeric chat ID allowlist is available and enforcing that allowlist before projection/materialization.

Consequences:

- more up-front feasibility work;
- new narrow command and interfaces;
- negative privacy tests become release blockers;
- server promotion is deferred.

Follow-ups:

- run live Windows feasibility probe;
- implement config and allowlisted reader contract;
- add Slack dry-run before real Slack;
- decide later whether a server wrapper is still needed.

## Planning Artifacts

- PRD: `.omx/plans/prd-windows-slack-monitor.md`
- Test spec: `.omx/plans/test-spec-windows-slack-monitor.md`
- Source requirements: `.omx/specs/deep-interview-automation-logic.md`

## Execution Handoff

Recommended first execution mode: `$ralph` if doing sequentially, `$team` if doing parallel.

Sequential order:

1. config and fail-closed tests;
2. allowlisted reader contract and fake reader;
3. cursor store;
4. polling service;
5. dry-run sink;
6. Slack sink;
7. CLI route;
8. verification gates.

Team lanes:

- `architect`: live Windows feasibility and stop/go gate.
- `executor`: C# implementation.
- `test-engineer`: SDK-only tests and verification scripts.
- `security-reviewer`: fail-closed/logging/no broad path review.
- `verifier`: build, fixture verification, dry-run/live checklist.

Launch hint:

```text
$team .omx/plans/windows-slack-monitor-consensus-plan.md
```

or

```text
$ralph .omx/plans/windows-slack-monitor-consensus-plan.md
```

Team verification path:

- team proves all unit/integration/negative gates;
- verifier runs build and fixture baseline;
- live Windows gate remains explicitly not-tested unless actually run on Windows KakaoTalk.

