# Comprexy OSS

Apache-2.0 OpenAI-compatible **Comprehension Proxy** for context management, token observability, and reproducible agent benchmarks across local and frontier workflows.

**Comprexy OSS** sits between your client (Cursor, CLI agents, custom apps) and any OpenAI-compatible upstream — local or frontier. It makes long sessions workable in three complementary ways:

- **Observable tokens** — conversation- and turn-level metrics (control-api, optional dashboard, telemetry MCP) so you can see what would have been sent, what was sent, and how compression behaved
- **Measurable quality** — a [benchmark harness](#benchmark-harness) and published [dogfood evidence](#dogfood-validation) for comparing compression setups on real coding workloads
- **Evidence for local ↔ frontier decisions** — the same signals support choosing when a local model is enough and when to move a workflow to a frontier endpoint (Comprexy does not auto-route)

Mechanically, it persists completed turns, rebuilds a bounded upstream prompt from versioned **working memory** plus still-unfolded messages, and folds older context via **Inline** wrap-up when soft budget pressure applies — without summarizing on every reply.

Under **Virtual Tools** (default), Comprexy OSS also owns the **model-facing tool catalog**: large IDE schemas (file read, shell, and similar) are replaced with compact `comprexy_*` IR tools, remapped to native client calls, and distilled on the way back — so tool definitions and results stop dominating the prompt. Optional `ExcludeFromModelTools` hides selected client tools from the model entirely.

Soft budget pressure triggers a blocking Inline follow-up wrap-up on eligible turns (closed stored tool chain + cooldown). Point `Provider` at Ollama, LM Studio, vLLM, or a cloud OpenAI-compatible endpoint.

[Project direction](#project-direction) · [Quick start](#quick-start) · [Why Comprexy OSS?](#why-comprexy-oss) · [Design principles](#design-principles) · [What Comprexy OSS is not](#what-comprexy-oss-is-not) · [Source of truth](#source-of-truth) · [Agentic workflow](#agentic-workflow) · [MCP setup](#mcp-setup) · [Features](#features) · [How it works](#how-it-works) · [Virtual Tools](#virtual-tools) · [Configuration](#configuration) · [Limitations](#limitations) · [Benchmark harness](#benchmark-harness) · [Architecture](#architecture) · [Contributing](#contributing)

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-cross--platform-informational)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)
![Status](https://img.shields.io/badge/status-open%20core-informational)

## Project direction

This repository is the **Apache 2.0–licensed open core** of Comprexy OSS. Feature work, bug fixes, documentation, and compatibility improvements are welcome here under the [Apache License 2.0](LICENSE).

Further product work may also continue as **Comprexy**, separate from this repository. The Comprexy name and branding for separate or commercial products remain subject to the [Trademark](#trademark) terms below.

> Comprexy OSS is the open core. Comprexy is the product.

## Quick start

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download). Metrics dashboard also needs [Node.js](https://nodejs.org/) (LTS).

```bash
git clone https://github.com/norielmallari/comprexy.git
cd comprexy
```

Configure upstream in `apps/proxy/appsettings.json`, or copy `appsettings.Local.json.example` → `appsettings.Local.json` for machine-local settings (preferred for keys):

```json
{
  "Provider": {
    "BaseUrl": "http://localhost:11434/v1",
    "ApiKey": null,
    "Model": "your-model"
  }
}
```

Omit `Model` (or set it `null`) to forward the client's `model` field instead. Inline wrap-up and ToolSchema mapping then reuse that same client model unless `Compression:Model` is set (mapper only).

```bash
./comprexy.sh proxy          # data plane :8129
./comprexy.sh control-api    # metrics + MCP :8130
./comprexy.sh dev            # proxy + control-api (Ctrl-C stops both)
```

Windows (PowerShell or cmd):

```bat
.\comprexy.cmd proxy
.\comprexy.cmd control-api
.\comprexy.cmd dev
```

Metrics dashboard (optional UI over control-api; run in a second terminal after control-api is up):

```bash
cd apps/dashboard
npm install
npm run dev                  # http://localhost:3000
```

Override the API base with `NEXT_PUBLIC_API_BASE_URL` if control-api is not on `http://localhost:8130`. Development CORS already allows `http://localhost:3000` in `apps/control-api/appsettings.Development.json` (and the Local example); for other hosts, set `Cors:AllowedOrigins` on control-api.

If .NET 10 is missing, the script prompts to install the SDK into `~/.dotnet` (or `%USERPROFILE%\.dotnet` on Windows) via the official Microsoft install script. Use `install-dotnet` or `COMPREXY_AUTO_INSTALL_DOTNET=1` for non-interactive installs.

On first run, Comprexy OSS applies EF Core migrations and creates `data/comprexy.db` under the repo root (shared with control-api). Listen URLs:

| Process | URL |
| --- | --- |
| Proxy | `http://localhost:8129` (`/v1/chat/completions`, …) |
| Control-api | `http://localhost:8130` (e.g. `GET /v1/comprexy/conversations`, MCP at `/mcp`) |
| Dashboard | `http://localhost:3000` (browser UI; talks to control-api) |

Equivalent `dotnet run --project apps/proxy` / `apps/control-api` commands still work; `./comprexy.sh help` / `.\comprexy.cmd help` lists shortcuts (`test`, `build`, `clear-db`).

Point any OpenAI-compatible client at:

```text
Base URL:  http://localhost:8129/v1
API key:   any value, or omit (or Auth:RequiredApiKey if set)
```

```bash
curl http://localhost:8129/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "client-model",
    "messages": [
      {"role": "system", "content": "You are a helpful coding assistant."},
      {"role": "user", "content": "Let'\''s build a REST API."}
    ]
  }'
```

On the normal path, when `Provider:Model` is set Comprexy OSS replaces `model` with that value; when it is null/omitted, the client's `model` is forwarded. In `Proxy:PassThrough` mode, the client body (including `model`) is forwarded as sent unless `Provider:Model` overrides it.

## Why Comprexy OSS?

Long-running agentic workflows — on local runtimes or frontier APIs — accumulate history, tool output, and corrections until each turn is noisy, expensive, or past the model’s useful window. On local runtimes, once the prompt crosses a size threshold, tokens-per-second often drops sharply. On frontier endpoints, the same growth drives cost and latency. Restarting and re-explaining kills flow; summarizing on every turn adds latency; blind truncation drops decisions you still need.

Comprexy OSS keeps the **sent** context manageable — stable information in versioned working memory, older context folded on soft budget pressure — so the model does not need the full accumulated history every turn. Coding agents also ship large `tools[]` catalogs (a single Shell definition can be thousands of tokens); Virtual Tools shrinks what the model sees without changing what the IDE executes. Token metrics and the [benchmark harness](#benchmark-harness) make those trade-offs inspectable, so you can compare setups and decide when a local model is enough versus when a frontier endpoint is warranted. Smaller upstream prompts do not guarantee faster inference or lower bills, but they help keep long sessions in a workable size band on either class of upstream.

### Dogfood validation

Top 3 evidences — end-to-end Cursor workflows on a local LLM (Qwen-35B behind Comprexy OSS):

1. **Dashboard implementation + tests (125 turns)** — continued `apps/dashboard/` (layout, chart polish; commit `5ca87ca`). About 10.35M baseline tokens → 5.19M sent-equivalent; after ~175k compression overhead, rollup net savings ~4.99M (48.24%). After working-memory folds, actual prompts stayed roughly 15–60k (under the ~64k comfort ceiling). Final turn ~124k → ~55k estimated tokens (247 raw → 76 sent; WM v3). Parent-session telemetry only (subagents not included). Evidence: [`docs/evidence/5ca87ca.md`](docs/evidence/5ca87ca.md) ([dashboard snapshot](docs/evidence/5ca87ca.png)).

2. **Earlier implementation (331 turns)** — built `apps/dashboard/` in one conversation (commit `721ea29`). About 66.05M baseline tokens → 10.21M sent-equivalent; after 7.47M compression overhead, rollup net savings ~48.37M (73.23%). After the first working-memory fold, actual prompts stayed mostly ~20–50k. Final analysis (last turn under 256k baseline): ~256k → ~35k estimated tokens. Evidence: [`docs/evidence/721ea29.md`](docs/evidence/721ea29.md).

3. **Planning (29 turns)** — produced the [Comprexy Metrics Dashboard implementation plan](docs/plans/comprexy-dashboard-implementation-plan.md). About 2.00M baseline tokens across the run → ~1.08M sent-equivalent (~800k saved). Final turn ~94k → ~37k estimated tokens (77 raw → 31 sent); effective prompts stayed roughly 21–58k. Evidence: [`docs/evidence/d2e0faa.md`](docs/evidence/d2e0faa.md).

These are dogfood workflows, not universal benchmarks — and they do not claim measured tok/s gains. Agent pipeline used for this work: [Agentic workflow](#agentic-workflow).

### Token and cost intelligence

Comprexy OSS includes conversation-level token and cost intelligence for long-running workflows.

For each conversation, it tracks estimated baseline token volume, sent-equivalent token volume, compression overhead, net tokens saved, savings ratios, working-memory versions, budget events, and per-turn prompt growth. That makes it easier to inspect what would have been sent without context management, what was actually sent upstream, and how compression behaved over time. Cost figures are estimate-based: apply a USD-per-1M-token rate to those token totals when you want a cost-equivalent signal. These signals support workflow inspection and tuning; they do not guarantee savings or ROI.

Approach:

| Goal | Approach |
| --- | --- |
| Stay in flow | Answer first; fold via Inline wrap-up on eligible soft-pressure turns so prompts stay smaller and sessions stay responsive longer |
| Preserve what matters | Persist completed turns; use versioned working memory for the active prompt, not blind truncation |
| Keep tool catalogs usable | Virtual Tools replace heavy file/shell schemas with short IR tools; optional `ExcludeFromModelTools` drops IDE UX tools the model should not see |
| Make tokens observable | Conversation- and turn-level metrics via control-api (dashboard / MCP); see [Token and cost intelligence](#token-and-cost-intelligence) |
| Measure quality | Reproducible [benchmark harness](#benchmark-harness) and published dogfood evidence — directional, not universal leaderboards |
| Support local ↔ frontier choices | Same OpenAI-compatible path and evidence surfaces for local or cloud upstreams; operators decide escalation — Comprexy does not auto-route |
| Stay compatible | OpenAI-compatible `/v1` base URL: chat completions are compressed; other `/v1/*` routes proxy upstream |
| Stay focused | Context compression and tool-surface management for chat completions — not a multi-provider gateway or agent framework |

If you need routing, spend tracking, or broad agent wrappers, tools like LiteLLM or Headroom may fit better. Comprexy OSS is intentionally narrower: chat-completion context management (including Virtual Tools) only.

## Design principles

- Answer first; fold on soft pressure when the stored tool chain is closed and cooldown allows.
- Persist the durable transcript; treat working memory as a derived, versioned prompt aid.
- Rebuild outgoing context from stored turns — do not forward an unmanaged client history as the model transcript.
- When Virtual Tools is on, own the model-facing tool contract: compact IR outbound, native remap to the client, distilled IR observations in the stored transcript.
- Prefer inspectable, deterministic behavior over opaque truncation — tokens, benches, and evidence before guesswork.
- Stay OpenAI-compatible for local and frontier upstreams; stay narrow (context compression and tool-surface management, not a gateway or agent framework).

## What Comprexy OSS is not

- Not a model or LLM runtime — it proxies to your configured upstream.
- Not a multi-provider gateway, router, or billing layer.
- Not a vector database or retrieval framework.
- Not a static prompt minifier or offline context packer.
- Not a guarantee of better answers or higher tok/s; it manages prompt size and structure so long sessions stay usable.
- Not a guarantee of agent quality, model correctness, code correctness, workflow success, or actual cloud bill reduction.

## Source of truth

Comprexy OSS persists completed conversation turns as the durable record. Working memory is a derived, versioned representation used to construct bounded upstream prompts. Compression marks messages as folded; it does not delete or replace them.

Soft pressure above `SoftLimitTokens` triggers a blocking Inline wrap-up on eligible turns (closed stored chain, or mid-chain closed-prefix checkpoint). The wrap-up folds older unfolded messages into a new working-memory version while retaining a tip window (`CompressionRetainMessageCount`).

Release notes: [`docs/release-notes/`](docs/release-notes/).

## Agentic workflow

This repository was developed with a Cursor subagent pipeline (plan → adversarial plan review → track-specific implement → unit test → adversarial review; UI adds mocked Playwright authorship then simulate), coordinated by orchestrators and handed off through files under `.cursor/agent-state/`. Every approved plan declares `track: backend | ui | mixed`. The same upstream that struggles past ~64k prompt tokens — local or frontier — stays usable longer because Comprexy OSS bounds what the model actually sees.

That loop produced the top 3 dogfood evidences ([`docs/evidence/5ca87ca.md`](docs/evidence/5ca87ca.md), [`docs/evidence/721ea29.md`](docs/evidence/721ea29.md), [`docs/evidence/d2e0faa.md`](docs/evidence/d2e0faa.md)). Agents, gates, and handoff rules: [`.cursor/README.md`](.cursor/README.md).

## MCP setup

Control-api exposes read-only conversation telemetry over MCP Streamable HTTP. Any IDE, coding agent, or MCP client that supports remote Streamable HTTP can connect to:

```text
http://localhost:8130/mcp
```

Start control-api directly or alongside the proxy:

```bash
./comprexy.sh control-api
# or
./comprexy.sh dev
```

Add a remote MCP server named `comprexy-telemetry` in your client's MCP settings using that URL. For clients that use the common `mcpServers` JSON shape:

```json
{
  "mcpServers": {
    "comprexy-telemetry": {
      "url": "http://localhost:8130/mcp"
    }
  }
}
```

For Cursor, the repository includes this configuration in `.cursor/mcp.json`. Other IDEs may use a different settings file or UI; use the same server URL and Streamable HTTP transport.

Reload the MCP client after starting control-api. If `Auth:RequiredApiKey` is configured, add an HTTP header through the client's secret or environment-variable support:

```json
{
  "mcpServers": {
    "comprexy-telemetry": {
      "url": "http://localhost:8130/mcp",
      "headers": {
        "Authorization": "Bearer <api-key>"
      }
    }
  }
}
```

Telemetry MCP tools are named `comprexy_*` and require `conversationId` from the proxy meta-tool `comprexy_get_current_conversation_id` (or from operator tooling / response header `X-Comprexy-Conversation-Id`).

## Features

| Feature | Description |
| --- | --- |
| OpenAI-compatible `/v1` | `POST /v1/chat/completions` is compressed (roles: `system` / `user` / `assistant` / `tool`). Other `/v1/*` routes reverse-proxy to `Provider` unchanged |
| Token metrics API | Control API `GET /v1/comprexy/conversations` (+ `/metrics`, `/metrics/turns`) on `:8130` reports raw vs compressed token savings per conversation |
| Metrics dashboard | Optional Next.js UI in `apps/dashboard` (`:3000`) over control-api REST; requires Node.js (LTS) |
| Telemetry MCP | Control API `/mcp` exposes read-only summaries, turns, compression phases, budget events, prompt growth, comparisons, evidence markdown, and conversation retrieval (search / message window / working memory / open tool chains) to MCP clients |
| Token and cost intelligence | Conversation-level telemetry for estimated baseline tokens, sent-equivalent tokens, compression overhead, net savings, prompt growth, and final-turn snapshots; metrics reads default to `Metrics:PromptTokenBasis=ProviderActual` (prefer upstream `usage`); optional USD-at-rate cost-equivalent estimates from those token totals |
| Rolling working memory | Versioned compressed representation of older context for prompt reconstruction. Derived from persisted messages via Inline wrap-up |
| Soft budget | Soft (`> soft`) → Inline follow-up wrap-up on eligible turns (`MinTurnsBetweenGenerations` cooldown): closed stored tool chain, or mid-chain checkpoint of a repairable closed prefix. Token estimates use tiktoken for text and OpenAI-style vision tiles for `image_url` (base64 is not BPE-counted) |
| Context rebuild | Outgoing context is always rebuilt from stored turns (IR-side under Virtual Tools). Working memory is omitted until the first successful compression; `Proxy:PassThrough` is the only full bypass |
| Virtual Tools | Default `ToolSchema:Mode=Virtual`. Maps the client catalog once per hash; model sees bound `comprexy_read_file_*` / `comprexy_dir_list` / `comprexy_shell` + meta + remaining passthrough; planner remaps to native tools; results distill to honest IR observations (span / completeness disclosure). Set `Off` (or use `Proxy:PassThrough`) to disable |
| Tool denylist | `ToolSchema:ExcludeFromModelTools` hides listed client tools from the model (case-insensitive; stock defaults include Cursor UX tools plus Kilo `agent_manager` / `agent_manager_models` / `background_process` / `kilo_local_recall`), rejects calls locally, and swallows inbound orphans. The subagent delegation tool (`task` / `Task`) is not denylisted, so agent delegation stays available |
| Conversation identity | Prefer a unique `X-Comprexy-Conversation-Id` per session; otherwise fingerprint from system + first two **plain** user turns (Cursor `<user_query>` when present; tool-echo user turns skipped) |
| Local-first, cloud-ready | Point `Provider` at Ollama, LM Studio, vLLM, OpenAI, Azure OpenAI–compatible APIs, and similar |
| Optional separate compression model | Use a cheaper/faster model for compression via `Compression` settings |
| Pass-through mode | `Proxy:PassThrough` forwards the original body unmodified — no rebuild or compression. Escape hatch only; leave off for normal use |
| Strip reasoning | `Proxy:StripReasoningContent` (default off) removes `reasoning_content` / `reasoning` from outbound chat and compression messages when enabled |
| Request audit files | Optional per-request / per-compression logs under `logs/requests/` (opt in via `appsettings.Local.json`) |
| Local persistence | Persists completed conversation turns, working-memory versions, metrics, and compression history. Persisted message records remain available after folding |

## How it works

```mermaid
flowchart LR
  Client[LLM client] --> Proxy["Comprexy OSS /v1/chat/completions"]
  Proxy --> Store[(Persisted turns)]
  Store --> Budget{Soft budget}
  Budget -->|under soft| Rebuild[Prompt rebuild]
  Budget -->|above soft after reply + eligible| SoftPath[Inline wrap-up]
  SoftPath --> WM[(Versioned working memory)]
  WM --> Rebuild
  Store --> Rebuild
  Rebuild --> Upstream[Upstream chat model]
  Upstream --> Client
```

**Normal path:** rebuild prompt (and, when Virtual is active, rewrite `tools[]`) → forward → return (or stream; remap/distill tool rounds as needed) → if above soft limit and eligible (closed stored tool chain + cooldown), run blocking Inline wrap-up under the exclusive conversation gate. Mid-chain turns may checkpoint the closed stored prefix while leaving an open assistant unfolded. Soft failure never overwrites last known-good working memory.

**After working memory exists:** outgoing context is roughly `system + working memory + still-unfolded messages + current tip`. The retain window is applied at Inline fold time.

### What compression does

Compression in Comprexy OSS:

- Reduces the active upstream prompt.
- Creates versioned working memory.
- Marks represented messages as folded.
- Retains persisted message records.

Compression does not delete persisted turns, replace the durable transcript, or wait for summarization on every reply.

## Virtual Tools

Coding agents often attach large OpenAI-compatible `tools[]` / `functions` catalogs. Under Virtual Tools, Comprexy OSS owns the **model-facing** contract while the IDE still executes **native** tools:

```text
Client tools[] → catalog hash + mapper → model IR tools
  → planner remap → native tool_calls → client executes
  → distill → IR observation in stored / model transcript
```

| Piece | Behavior |
| --- | --- |
| File family | Bound `comprexy_read_file_manifest` / `range` / `search` and `comprexy_dir_list` replace native Read/Grep/list backends; optional `end_line` enables an unwindowed first read (capped by `FirstReadMaxLines` / `FirstReadMaxChars`) |
| Shell family | Bound `comprexy_shell` replaces native Shell/bash backends |
| Observations | Distilled IR discloses requested vs returned spans, `body_complete` / `complete` / `next_start_line`, and related search/dir honesty fields; caps are also described on Virtual tool schemas |
| Cache | File-body cache tracks `BodyComplete` / `TotalLineCount`; incomplete entries rematerialize (never local-satisfy). Local-satisfy IR turns are still persisted |
| Shapes | First-result shape probes always run; optional idle learner (`ToolSchema:ResultShape:Learner`, default off) may promote closed `result_shapes` into MappingJson |
| Denylist | `ExcludeFromModelTools` omits listed client tools from the model catalog |
| Meta | `comprexy_get_current_conversation_id` runs proxy-locally |
| Failure | Mapper exhaustion drops only the bindings that failed validation and keeps the rest; if nothing usable survives, `ToolIrDisabled` is set for that catalog hash and client tools are forwarded unchanged. Compression/budgets stay on either way |

Runtime detail: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#tool-schema-virtual-tools). Options: [`docs/SETTINGS.md`](docs/SETTINGS.md#toolschema).

## Configuration

Settings load from `appsettings.json`, environment overlays, and optional gitignored `appsettings.Local.json`. See **[`docs/SETTINGS.md`](docs/SETTINGS.md)** for the full reference (Provider, Compression, ContextPolicy, **ToolSchema**, Metrics, Auth, Proxy, Trace, token cache, SQLite).

| Section | Role (summary) |
| --- | --- |
| `Provider` | Upstream OpenAI-compatible chat endpoint |
| `Compression` | Optional separate Compression endpoint for ToolSchema mapper; Inline wrap-up prompts |
| `ContextPolicy` | Soft token limit, Inline cooldown / retain tip (mid-chain prefix when eligible) |
| `ToolSchema` | Virtual Tools (`Mode: Virtual` default), file/shell IR, `ExcludeFromModelTools`, observation/cache TTLs, `FirstRead*`, optional `ResultShape` learner |
| `Metrics` | Read-side `PromptTokenBasis` (`ProviderActual` default); SoftBudget persistence stays estimate-based |
| `McpTelemetry` | Control-api MCP row limits and query timeout |
| `Auth` | Optional API key gate on `/v1/*` and control-api `/mcp` |
| `Proxy` | Pass-through and reasoning strip |
| `Trace` | Console payload trace and request audit files |
| `ConnectionStrings:Comprexy` | SQLite path |

**Conversation id:** prefer `X-Comprexy-Conversation-Id` per session; otherwise fingerprint from system + first two plain user turns (see SETTINGS.md).

## Limitations

- Chat compression supports `system`, `user`, `assistant`, and `tool` roles. Other roles (for example `developer`) are rejected on `/v1/chat/completions`.
- Without `X-Comprexy-Conversation-Id`, conversation identity is a text fingerprint of the system prompt and first two **plain** user turns (Cursor `<user_query>` extraction when present; tool-echo user turns skipped). Use an explicit id for multi-tab or multi-user setups.
- After working memory exists, the system prompt captured on the first turn is reused when rebuilding context.
- `Proxy:PassThrough` disables context management entirely.
- Soft Inline wrap-up and the conversation gate are process-local; they are not shared across multiple API instances.
- Virtual Tools mapping is best-effort per catalog hash. A catalog with no tool the mapper can bind to a given Virtual tool loses that Virtual tool only; when nothing usable survives, Comprexy OSS sets `ToolIrDisabled` and forwards native tools for that hash (compression stays on).
- Incomplete file-body cache entries never local-satisfy; the proxy rematerializes until a complete body is cached (often via an unwindowed first read).
- `ExcludeFromModelTools` hides tools from the model only; they remain in the client catalog. Already-persisted transcript turns are not scrubbed.
- SoftBudget persistence and wrap-up eligibility stay estimate-based (tiktoken). Metrics API reads default to `PromptTokenBasis=ProviderActual` when upstream `usage.prompt_tokens` is present. Actual provider billing may still differ because of model-specific tokenization, prompt caching, output volume, provider pricing, local hardware utilization, and workflow shape.

## Benchmark harness

`tests/Comprexy.Bench` replays a frozen prompt list through a Microsoft Agent Framework coding agent twice — once with client-side compaction alone (`ToolSchema:Mode=Off`, unreachable soft limit) and once with Comprexy compression plus Virtual Tools — against harness-spawned proxy and control-api hosts on a dedicated `data/comprexy-bench.db`. The agent works in a throwaway `git clone` of this repository pinned to the run's HEAD commit, so both arms read the same real code; the clone has no remote and its own object store, and it is deleted after each conversation with its diff against the pinned commit kept as a patch.

By default, if `maf-compact` dies of a provider/context failure after X prompts (HTTP 502, completion stall, context overflow), the `comprexy` arm stops once it completes X+1 (`survived_baseline_failure`) instead of finishing the script — clearing the kill zone is the result. Opt out with `--continue-past-baseline-failure` (optional `--survival-margin <n>`).

```bash
./comprexy.sh bench run                              # spawn hosts, run both arms, write manifest.json
./comprexy.sh bench report --run-id <runId>          # join control-api metrics, draft summary.md
./comprexy.sh bench publish --run-id <runId> --confirm  # copy the reviewed summary to docs/evidence/
```

Each run writes to a gitignored directory named for the UTC minute it started, `reports/bench/20260801-1200/`, so a repeat never overwrites earlier artifacts; `--run-id <label>` appends a label to that stamp (`20260801-1200-short-deep`), and `report` and `publish` take the resulting directory name. Only reviewed summaries are committed. Token numbers come from Comprexy's own turn metrics, so a run needs a configured provider and enough wall clock for two full passes.

## Architecture

Layering, request lifecycle, Virtual Tools, compression ownership, and persistence are documented in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Security

Treat API keys and request audit logs as sensitive. Prefer `appsettings.Local.json`, environment variables, or user secrets for `Provider:ApiKey`, `Compression:ApiKey`, and `Auth:RequiredApiKey`. Comprexy OSS forwards traffic only to the configured upstream(s) — review those endpoints and what clients send. See [`CONTRIBUTING.md`](CONTRIBUTING.md#security) for contributor hygiene (what not to commit or share).

## AI-assisted development

Much of this repository was produced with AI coding assistants under human direction. Maintainers review and are responsible for what ships. See [`CONTRIBUTING.md`](CONTRIBUTING.md#ai-assisted-development) for how to treat PRs and docs.

## Contributing

Features, bug fixes, documentation, and compatibility improvements are welcome under the [Apache License 2.0](LICENSE) (see [Project direction](#project-direction)). Product branding remains subject to [Trademark](#trademark). See [`CONTRIBUTING.md`](CONTRIBUTING.md) for build, test, database, and migration notes.

## License

[Apache License 2.0](LICENSE). See also [`NOTICE`](NOTICE).

## Copyright

Copyright 2026 Noriel Mallari. See [`NOTICE`](NOTICE).

## Trademark

Comprexy™ is a trademark claimed by Noriel Mallari.

The Apache License 2.0 applies to the software source code in this repository (Comprexy OSS). It does not grant permission to use the Comprexy name, logo, or branding to identify, market, or promote any separate, modified, or derivative product (see also Apache License §6).

Forks and derivatives should use a distinct name unless written permission is granted.

Descriptive attribution such as “based on Comprexy OSS” is allowed, provided it does not imply official endorsement, sponsorship, or affiliation.