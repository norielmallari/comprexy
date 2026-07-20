# Agent state (handoff bus)

All orchestrator ↔ specialist handoffs go through files here — **not** chat.

## Layout

```text
.cursor/agent-state/<run-folder>/
  plan.md                 # planner draft / approved plan
  plan-review.md          # latest plan-reviewer output (overwrite each try)
  handoff.md              # implementer → unit-tester
  unit-test-result.md     # unit-tester success (or failure payload)
  code-review.md          # latest code-reviewer output (overwrite each try)
```

`<run-folder>` is a short kebab slug from the requirement/goal (e.g. `address-duplicate-logic`). Create the folder before try 1.

## Rules

1. Orchestrators resolve `<run-folder>` and pass **absolute or repo-relative paths** to specialists.
2. Specialists **must** write their full artifact to the assigned path; chat is a brief summary + path only.
3. Specialists **must** read prior artifacts from paths — do not rely on pasted chat bodies.
4. Durable copies under `internal/plans/` are optional and only when the human asks; the live handoff bus is always `.cursor/agent-state/`.

Runtime contents of this directory (except this README) are gitignored.
