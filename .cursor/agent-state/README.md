# Agent state (handoff bus)

All orchestrator ↔ specialist handoffs go through files here — **not** chat.

## Tracks

Every `plan-vX.md` must declare:

```markdown
track: backend | ui | mixed
```

| Track | Implementation orchestrator |
| --- | --- |
| `backend` | `backend-implementation-orchestrator` |
| `ui` | `ui-implementation-orchestrator` |
| `mixed` | Backend run folder/slice first, then UI — not one mega-loop |

## Layout

```text
.cursor/agent-state/<run-folder>/
  plan-v1.md                 # planner draft for try 1 (includes track:)
  plan-review-v1.md          # plan-reviewer output for try 1
  plan-v2.md                 # revised draft for try 2; prior versions remain immutable
  plan-review-v2.md          # plan-reviewer output for try 2
  handoff-v1.md              # implementer handoff for implementation try 1
  unit-test-result-v1.md     # unit-tester result for implementation try 1
  code-review-v1.md          # code review for implementation try 1
  ui-sim-result-v1.md        # UI simulator result for implementation try 1

# bench-runner ops (not a product track) — e.g. bench-20260805/
  bench-queue.md          # ordered scripts + done/failed/pending
  bench-run-<script>.md   # per-script run/report outcome
  bench-ledger.md         # rolling paired/survival/excluded + publish gate
```

`<run-folder>` is a short kebab slug from the requirement/goal (e.g. `address-duplicate-logic`, `bench-20260805`). Create the folder before try 1.

## Rules

1. Orchestrators resolve `<run-folder>` and pass **absolute or repo-relative paths** to specialists.
2. Every loop try uses the same 1-based numeric suffix across its artifacts: `-v1`, `-v2`, or `-v3`. The suffix is appended immediately before `.md`.
3. Versioned artifacts are immutable. Before a specialist starts, its assigned output path **must not exist**. Never overwrite, truncate, copy over, or reuse a prior `-vX` artifact.
4. On a retry, specialists read the prior version and write the next version. For example, a planner reads `plan-v1.md` + `plan-review-v1.md` and writes `plan-v2.md`; an implementer reads the approved plan plus prior `code-review-v1.md` / `unit-test-result-v1.md` and writes `handoff-v2.md`.
5. There are no unversioned “latest” aliases. Orchestrators report and pass the exact approved/current versioned paths.
6. A failed structure or handoff gate ends that try. Corrections use the next `-vX` set and consume the next retry; never overwrite an incomplete artifact.
7. A new orchestration stage must start in a folder/slice with no versioned artifacts for that stage. If matching `-vX` files already exist, choose a new run folder/slice rather than continuing at `-v4` or replacing history.
8. Mixed work uses separate backend and UI run folders/slices so their independent `-v1` sequences cannot collide.
9. Specialists **must** write their full artifact to the assigned path; chat is a brief summary + path only.
10. Specialists **must** read prior artifacts from paths — do not rely on pasted chat bodies.
11. Durable copies under `internal/plans/` are optional and only when the human asks; the live handoff bus is always `.cursor/agent-state/`.

Runtime contents of this directory (except this README) are gitignored.
