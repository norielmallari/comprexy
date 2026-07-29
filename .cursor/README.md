# Agentic workflow

Cursor subagents used to plan and ship Comprexy changes on a **local LLM**, with Comprexy itself compressing the long agent loops so prompts stay in a workable size band.

This is dogfooding: the same compression proxy that the product provides is what makes these multi-agent runs practical when the upstream model slows past ~64k prompt tokens.

## Why file-backed agents

Chat-only multi-agent handoffs blow up context and lose structure. This workflow:

1. Keeps **specialists narrow** (plan, review, implement, test, adversarial review, UI simulate)
2. Passes state through **files** under [`.cursor/agent-state/`](agent-state/README.md), not pasted chat bodies
3. Relies on Comprexy to bound the **upstream** prompt while the durable transcript and handoff files grow

Orchestrators coordinate; specialists do the work. Fresh subagent instances each loop try (no resume across tries). Max three tries per gate, then human-in-the-loop.

## Pipeline

```text
requirement / finding
        │
        ▼
┌───────────────────────┐
│  plan-orchestrator    │  loops planner → plan-reviewer (≤3)
└───────────┬───────────┘
            │ approved plan.md (track: backend | ui | mixed)
            ▼
     ┌──────┴──────┐
     │             │
     ▼             ▼
┌────────────┐  ┌────────────────────┐
│ backend-   │  │ ui-implementation- │
│ implement. │  │ orchestrator       │
│ orchestr.   │  │                    │
└─────┬──────┘  └─────────┬──────────┘
      │                   │
      │ backend-          │ ui-implementer →
      │ implementer →     │ ui-unit-tester →
      │ backend-unit-     │ ui-reviewer →
      │ tester →          │ ui-simulator
      │ backend-code-     │
      │ reviewer          │
      ▼                   ▼
   ship / HITL         ship / HITL
```

| Stage | Orchestrator | Specialists |
| --- | --- | --- |
| Plan | `plan-orchestrator` | `planner`, `plan-reviewer` |
| Build (backend) | `backend-implementation-orchestrator` | `backend-implementer`, `backend-unit-tester`, `backend-code-reviewer` |
| Build (UI) | `ui-implementation-orchestrator` | `ui-implementer`, `ui-unit-tester`, `ui-reviewer`, `ui-simulator` |

Every approved `plan.md` must declare `track: backend | ui | mixed`. Mixed work runs **backend then UI** (separate run folders or sequenced tries).

If only a requirement exists, start with **plan-orchestrator**. If an approved plan already exists, start with the implementation orchestrator for that plan’s `track`.

### Interim Task slug

`unit-tester.md` remains a thin stub: resolve track, then follow `backend-unit-tester` or `ui-unit-tester`. Prefer launching those agents directly.

## Agents

Agent prompts live under [`.cursor/agents/`](agents/).

| Agent | Role |
| --- | --- |
| [`plan-orchestrator`](agents/plan-orchestrator.md) | Coordinates plan approval; routes by `track`; does not author plan body or product code |
| [`planner`](agents/planner.md) | Turns a requirement into an implementable plan (must set `track`) |
| [`plan-reviewer`](agents/plan-reviewer.md) | Adversarial plan gate — rejects incomplete plans / missing track |
| [`backend-implementation-orchestrator`](agents/backend-implementation-orchestrator.md) | Backend implement → test → review until approval or HITL |
| [`backend-implementer`](agents/backend-implementer.md) | Backend production code; `dotnet build` must pass; no unit tests |
| [`backend-unit-tester`](agents/backend-unit-tester.md) | Backend xUnit from handoff; UI track redirects to `ui-unit-tester` |
| [`backend-code-reviewer`](agents/backend-code-reviewer.md) | Adversarial backend review (DI/lease); read-only |
| [`ui-implementation-orchestrator`](agents/ui-implementation-orchestrator.md) | UI implement → unit+e2e author → ui-review → ui-sim until approval or HITL |
| [`ui-implementer`](agents/ui-implementer.md) | UI production code; `npx tsc --noEmit` clean + build; handoff for Vitest + Playwright smokes |
| [`ui-unit-tester`](agents/ui-unit-tester.md) | Vitest/RTL **and** mocked Playwright fixtures/smokes from handoff; keeps `npx tsc --noEmit` clean |
| [`ui-reviewer`](agents/ui-reviewer.md) | Adversarial UI review (typecheck, a11y, locators, test authorship); read-only |
| [`ui-simulator`](agents/ui-simulator.md) | Runs committed Playwright under existing mocks; no new fixture invention |

Each canonical agent file is the source of truth for gates, chat brevity, and artifact paths.

On the UI track, every stage that touches TypeScript must run `npx tsc --noEmit` from the app package root with zero errors. `ui-reviewer` verifies it independently instead of trusting the handoff, and no stage may clear it with `any` or `@ts-ignore` suppressions.

Durable UI invariants (not runbooks): [`.cursor/rules/ui-accessibility.mdc`](rules/ui-accessibility.mdc), [`ui-testing.mdc`](rules/ui-testing.mdc), [`ui-fixtures.mdc`](rules/ui-fixtures.mdc).

## Handoff bus

Runtime artifacts live under `.cursor/agent-state/<run-folder>/` (gitignored except the [state README](agent-state/README.md)):

| File | Writer |
| --- | --- |
| `plan.md` | planner (`track:` required) |
| `plan-review.md` | plan-reviewer |
| `handoff.md` | backend-implementer / ui-implementer → backend-unit-tester or ui-unit-tester |
| `unit-test-result.md` | backend-unit-tester / ui-unit-tester |
| `code-review.md` | backend-code-reviewer or ui-reviewer |
| `ui-sim-result.md` | ui-simulator (UI track; run-only) |

Specialists write the full artifact to disk and keep chat short (paths + summary). Orchestrators and the next specialist **read the files**.

## Local LLM fit

These agents assume `model: inherit` — in practice, the Cursor chat’s upstream (often a local OpenAI-compatible model behind Comprexy).

Design choices that matter for local models:

- **Narrow roles** — smaller prompts and clearer success criteria than a single “do everything” agent
- **File handoffs** — long plans and reviews do not have to round-trip in every specialist’s chat context
- **Comprexy on the proxy path** — keeps sent prompts roughly in a 20–50k band after working memory exists, under the ~64k comfort ceiling where this local setup stays responsive

Without compression, baseline history in long orchestrated runs climbs well past that ceiling; the agent loop becomes too slow to finish.

## Dogfood evidence

Top 3 evidences:

| Run | What | Evidence |
| --- | --- | --- |
| Dashboard implementation + tests | 125-turn dashboard continuation on Qwen-35B (commit `5ca87ca`) | [`docs/evidence/5ca87ca.md`](../docs/evidence/5ca87ca.md) |
| Earlier implementation | 331-turn dashboard build on Qwen-35B (commit `721ea29`) | [`docs/evidence/721ea29.md`](../docs/evidence/721ea29.md) |
| Planning | 29-turn plan for the metrics dashboard | [`docs/evidence/d2e0faa.md`](../docs/evidence/d2e0faa.md) |

These runs kept actual prompt tokens under the ~64k local comfort ceiling after working-memory folds while baseline history grew much larger. That is the practical claim: **agentic workflows on a local LLM that would otherwise stall**.

## How to use

1. Put Comprexy in front of the local upstream (`./comprexy.sh dev` or proxy + control-api).
2. Point Cursor’s OpenAI-compatible base URL at the proxy.
3. For a new requirement: invoke **plan-orchestrator** (or ask the parent agent to).
4. After plan approval: invoke **backend-implementation-orchestrator** (`track: backend`) or **ui-implementation-orchestrator** (`track: ui`) with the same `.cursor/agent-state/<run-folder>/`. For `mixed`, finish backend then start the UI orchestrator.
5. On third-try failure at either gate: stop for human review — do not invent a fourth automatic loop.

Optional durable copies of plans under `internal/plans/` are human-requested only; the live bus is always `.cursor/agent-state/`.
