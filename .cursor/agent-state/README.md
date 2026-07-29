# Agent state (handoff bus)

All orchestrator ↔ specialist handoffs go through files here — **not** chat.

## Tracks

Every `plan.md` must declare:

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
  plan.md                 # planner draft / approved plan (includes track:)
  plan-review.md          # latest plan-reviewer output (overwrite each try)
  handoff.md              # backend-implementer → backend-unit-tester; ui-implementer → ui-unit-tester
  unit-test-result.md     # backend-unit-tester / ui-unit-tester success (or failure payload)
  code-review.md          # backend-code-reviewer or ui-reviewer (overwrite each try)
  ui-sim-result.md        # ui-simulator (UI track only; run committed Playwright — no new mock authorship)
```

Optional: `ui-handoff.md` if a run needs a UI-specific handoff template distinct from `handoff.md` (default remains `handoff.md`).

`<run-folder>` is a short kebab slug from the requirement/goal (e.g. `address-duplicate-logic`). Create the folder before try 1.

## Rules

1. Orchestrators resolve `<run-folder>` and pass **absolute or repo-relative paths** to specialists.
2. Specialists **must** write their full artifact to the assigned path; chat is a brief summary + path only.
3. Specialists **must** read prior artifacts from paths — do not rely on pasted chat bodies.
4. Durable copies under `internal/plans/` are optional and only when the human asks; the live handoff bus is always `.cursor/agent-state/`.

Runtime contents of this directory (except this README) are gitignored.
