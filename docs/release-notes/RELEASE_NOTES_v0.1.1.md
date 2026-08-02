# Comprexy OSS v0.1.1

First post-preview release of the Comprexy OSS open core. Builds on `v0.1.0-preview` with Virtual Tools, a split data/control plane, token metrics, and operator telemetry.

This repository is the Apache 2.0–licensed open core of Comprexy OSS. Further product work may also continue separately as Comprexy.

## Highlights

- **Virtual Tools (default)** — Replaces CompactIndex. Large IDE tool schemas (file read, dir list, shell) are mapped to compact `comprexy_*` IR tools for the model, remapped to native client calls, and distilled on the way back. Optional `ToolSchema:ExcludeFromModelTools` hides selected client tools from the model catalog.
- **Inline-only wrap-up** — Soft budget pressure folds older context via blocking Inline wrap-up only (closed stored tool chain + cooldown). Live-model retain mode keeps a tip window in working memory.
- **Split apps** — Proxy (`:8129`) owns chat completions; control-api (`:8130`) owns metrics REST and telemetry MCP. Both share SQLite at `data/comprexy.db`.
- **Token metrics + dashboard** — Per-conversation and per-turn token ledger via control-api; optional Next.js UI in `apps/dashboard` (`:3000`).
- **Telemetry MCP** — Read-only `comprexy_*` tools over Streamable HTTP at `http://localhost:8130/mcp` for conversation inspection, compression phases, budget events, and evidence markdown.

## Added

- Virtual Tools for file and shell families, plus tool denylist (`ExcludeFromModelTools`)
- Control-api metrics endpoints (`GET /v1/comprexy/conversations`, `/metrics`, `/metrics/turns`)
- Metrics dashboard with Playwright smoke coverage
- Telemetry / retrieval MCP endpoint on control-api
- Per-turn token metrics ledger and conversation-level token/cost intelligence
- Process-local KV prefix for wrap-up-ready context
- StrReplace loop damping: failed-edit dedupe, hot-path retain, and working-memory Rules pins
- Inline retain mode for live-model working memory
- Client `model` forwarding when `Provider:Model` is null/omitted
- Vision-tile token estimates for `image_url` (base64 is not BPE-counted)
- Guid PK + ClusterId surrogate entity base
- Agentic workflow docs and dogfood compression evidence under `docs/evidence/`

## Changed

- ToolSchema default is Virtual Tools (`Mode: Virtual`); CompactIndex removed
- Compression path is Inline wrap-up only
- `Proxy:StripReasoningContent` defaults to `false`
- Hard / compression limits are nullable where appropriate
- Conversation fingerprinting prefers plain user turns (Cursor `<user_query>` when present; tool-echo user turns skipped)
- README and `docs/ARCHITECTURE.md` / `docs/SETTINGS.md` updated for the current surface

## Fixed

- Skip tip sync-repair after Virtual Tools rewrite
- Protect tool-schema hydrate turns from compression fold
- Deduplicate `currentUserMessage` to avoid invalid tool-message sequences
- Keep assistant/tool pairs atomic in retain selection
- Dashboard token chart and 2×2 metric card layout
- Pass client model into compression when `Provider:Model` is null

## Upgrade notes

- **Required from v0.1.0 / `v0.1.0-preview`:** delete the SQLite database before starting v0.1.1 (`data/comprexy.db`, or `./comprexy.sh clear-db` / `.\comprexy.cmd clear-db`). Schema changes are not upgrade-compatible; keeping the old DB will fail or behave incorrectly.
- Prefer `./comprexy.sh` / `.\comprexy.cmd` (`proxy`, `control-api`, `dev`) over a single monolithic host.
- Point OpenAI-compatible clients at `http://localhost:8129/v1` as before.
- For metrics UI: start control-api, then `cd apps/dashboard && npm install && npm run dev`.
- For MCP: connect Streamable HTTP clients to `http://localhost:8130/mcp` (see README).
- Review `ToolSchema` and `ContextPolicy` in [`docs/SETTINGS.md`](../SETTINGS.md) if you customized CompactIndex-era settings.