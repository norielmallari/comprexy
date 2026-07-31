<!-- Generated from .cursor/rules/agents-readme-sync.mdc — edit the source, not this file. -->

# Agents documentation sync

_Scope: `.cursor/agents/**`, `.cursor/README.md`_

When you add, remove, rename, or materially change agents under `.cursor/agents/` (roles, pipeline, gates, handoffs, or local-LLM assumptions), update both docs in the **same change**:

1. **[`.cursor/README.md`](.cursor/README.md)** — source of truth for the pipeline diagram, agent roster table, handoff bus, and how-to-use steps
2. **[`README.md`](README.md) § Agentic workflow** — short public summary + link to `.cursor/README.md`; keep the stage list aligned (plan → plan review → implement → unit test → code review; UI adds simulate)

## Do

- Add or remove roster rows when agent files are added or deleted
- Refresh the pipeline diagram / stage table if orchestrator↔specialist wiring changes
- Keep the main README section brief; put detail only in `.cursor/README.md`
- Preserve documentation tone (factual dogfood, not marketing)

## Do not

- Leave a new/renamed agent file undocumented in the `.cursor/README.md` roster
- Expand the main README into a second full agent guide
- Skip the main README link/summary when the public-facing workflow story changes

## Exception

Typo-only or comment-only edits inside a single agent prompt that do not change role, gates, artifacts, or pipeline wiring do not require README updates.
