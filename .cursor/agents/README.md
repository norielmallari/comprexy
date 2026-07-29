# Agentic workflow

Cursor subagents used to plan and ship Comprexy changes on a **local LLM**, with Comprexy itself compressing the long agent loops so prompts stay in a workable size band.

This is dogfooding: the same compression proxy that the product provides is what makes these multi-agent runs practical when the upstream model slows past ~64k prompt tokens.

## Why file-backed agents

Chat-only multi-agent handoffs blow up context and lose structure. This workflow:

1. Keeps **specialists narrow** (plan, review, implement, test, adversarial review)
2. Passes state through **files** under [`.cursor/agent-state/`](../agent-state/README.md), not pasted chat bodies
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
            │ approved plan.md
            ▼
┌───────────────────────┐
│ implementation-       │  loops implementer → unit-tester → code-reviewer (≤3)
│ orchestrator          │
└───────────┬───────────┘
            │ approved code-review.md
            ▼
        ship / HITL
```

| Stage | Orchestrator | Specialists |
| --- | --- | --- |
| Plan | `plan-orchestrator` | `planner`, `plan-reviewer` |
| Build | `implementation-orchestrator` | `implementer`, `unit-tester`, `code-reviewer` |

If only a requirement exists, start with **plan-orchestrator**. If an approved plan already exists, start with **implementation-orchestrator**.

## Agents

| Agent | Role |
| --- | --- |
| [`plan-orchestrator`](plan-orchestrator.md) | Coordinates plan approval; does not author plan body or product code |
| [`planner`](planner.md) | Turns a requirement into an implementable plan (inventory, DI/lifecycle, impact, tests) |
| [`plan-reviewer`](plan-reviewer.md) | Adversarial plan gate — rejects incomplete or contradictory drafts |
| [`implementation-orchestrator`](implementation-orchestrator.md) | Coordinates implement → test → review until approval or HITL |
| [`implementer`](implementer.md) | Plan-driven production code; build must pass; no unit tests |
| [`unit-tester`](unit-tester.md) | Writes/updates tests from implementer handoff; drives suite green |
| [`code-reviewer`](code-reviewer.md) | Adversarial plan-gated review of the diff; read-only verdict |

Each agent file is the source of truth for gates, chat brevity, and artifact paths.

## Handoff bus

Runtime artifacts live under `.cursor/agent-state/<run-folder>/` (gitignored except the [state README](../agent-state/README.md)):

| File | Writer |
| --- | --- |
| `plan.md` | planner |
| `plan-review.md` | plan-reviewer |
| `handoff.md` | implementer → unit-tester |
| `unit-test-result.md` | unit-tester |
| `code-review.md` | code-reviewer |

Specialists write the full artifact to disk and keep chat short (paths + summary). Orchestrators and the next specialist **read the files**.

## Local LLM fit

These agents assume `model: inherit` — in practice, the Cursor chat’s upstream (often a local OpenAI-compatible model behind Comprexy).

Design choices that matter for local models:

- **Narrow roles** — smaller prompts and clearer success criteria than a single “do everything” agent
- **File handoffs** — long plans and reviews do not have to round-trip in every specialist’s chat context
- **Comprexy on the proxy path** — keeps sent prompts roughly in a 20–50k band after working memory exists, under the ~64k comfort ceiling where this local setup stays responsive

Without compression, baseline history in long orchestrated runs climbs well past that ceiling; the agent loop becomes too slow to finish.

## Dogfood evidence

| Run | What | Evidence |
| --- | --- | --- |
| Planning | 29-turn plan for the metrics dashboard | [`docs/evidence/d2e0faa.md`](../../docs/evidence/d2e0faa.md) |
| Implementation | 331-turn dashboard build on Qwen-35B (commit `721ea29`) | [`docs/evidence/721ea29.md`](../../docs/evidence/721ea29.md) |

The implementation run kept actual prompt tokens mostly in the ~20–50k range after the first working-memory fold while baseline history grew past 250k. That is the practical claim: **agentic workflows on a local LLM that would otherwise stall**.

## How to use

1. Put Comprexy in front of the local upstream (`./comprexy.sh dev` or proxy + control-api).
2. Point Cursor’s OpenAI-compatible base URL at the proxy.
3. For a new requirement: invoke **plan-orchestrator** (or ask the parent agent to).
4. After plan approval: invoke **implementation-orchestrator** with the same `.cursor/agent-state/<run-folder>/`.
5. On third-try failure at either gate: stop for human review — do not invent a fourth automatic loop.

Optional durable copies of plans under `internal/plans/` are human-requested only; the live bus is always `.cursor/agent-state/`.
