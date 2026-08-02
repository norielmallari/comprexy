# Benchmark Harness — Microsoft Agent Framework

## Problem

The current harness sends 80 dummy tools in a bloated schema, but the LLM never calls them. Without real tool calls, we measure compression of nothing — no tool result distillation, no working memory pressure from large payloads, no tool IR remap, no cache alignment. Fake tools don't exercise the compression pipeline.

## Solution

Use Microsoft Agent Framework (Agent SDK) as the agent in the harness. MAF provides built-in tools (`read_file`, `write_file`, `search_files`, `list_directory`, `shell_command`, etc.) with real execution. The harness becomes a true black box — it sends prompts, the SDK handles tool calling and multi-turn loops, the proxy compresses everything, and we measure.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  Harness (MAF Agent SDK)                            │
│  ┌─────────────────────────────────────────────┐    │
│  │  Conversation: array of user prompts        │    │
│  │  - long-planning.json                       │    │
│  │  - heavy-tool-usage.json                    │    │
│  │  - mixed-workload.json                      │    │
│  │  - short-deep.json                          │    │
│  │  - edge-case-noisy.json                     │    │
│  └─────────────────────────────────────────────┘    │
│                                                     │
│  MAF Agent:                                         │
│  - Sends prompts to proxy                           │
│  - Handles tool calls (built-in tools)              │
│  - Manages multi-turn loops                         │
│  - No knowledge of compression                      │
└──────────────────────┬──────────────────────────────┘
                       │ chat completions API
                       ▼
┌─────────────────────────────────────────────────────┐
│  Comprexy Proxy (:8129)                             │
│  - Receives prompts + tool schema (from MAF)        │
│  - LLM responds with text or tool_calls             │
│  - Compresses context (Virtual Tools, WM, Inline)   │
│  - Returns metrics via control-api (:8130)          │
└─────────────────────────────────────────────────────┘
```

## Conversation File Format

Each file is a JSON array of user prompt strings. The harness feeds them sequentially:

```json
[
  "Use the read_file tool to check docs/ARCHITECTURE.md and summarize the layer boundaries.",
  "Now list the Migrations folder and read the latest migration.",
  "Run a shell command to check git status.",
  ...
]
```

Between each prompt, the MAF agent drives the full tool-calling loop:
1. Send prompt + accumulated history
2. If LLM returns tool_calls → execute tools → send results → repeat
3. When LLM returns text response → move to next prompt
4. Accumulate everything in history

The harness doesn't know about tools, schemas, or the conversation flow. It just drives prompts through the agent.

## Implementation Plan

### Phase 1: MAF Integration (2-3 days)

- Add `Microsoft.Agents.*` NuGet packages to `Comprexy.Bench`
- Create `MaFConversationRunner.cs` — replaces `ConversationRunner.cs`
- Configure MAF agent with proxy URL (`:8130`) as the chat endpoint
- Wire up MAF's built-in tools (file, shell, search, directory)
- CLI parser stays the same (`--test`, `--all`, `--output`, `--help`)

### Phase 2: Conversation Content (1-2 days)

- Rewrite conversation files to use MAF tool names
- `long-planning.json` — 8 prompts referencing MAF tools (`read_file`, `list_directory`, `shell_command`)
- `heavy-tool-usage.json` — 100+ prompts with deep tool chains
- `mixed-workload.json` — planning + tool use mix
- `short-deep.json` — high-density tool usage
- `edge-case-noisy.json` — noisy debugging with retries

### Phase 3: Metrics Collection (1 day)

- Capture conversation ID from proxy response headers
- Query control-api (`:8130`) for per-turn metrics
- Aggregate: total tokens, savings ratio, compression events
- Output structured JSON

### Phase 4: Shell Script Integration (0.5 day)

- Add `run_maF_bench()` to `comprexy.sh`
- Add `Invoke-MaFBench` to `comprexy.ps1`
- Thin shim in `comprexy.cmd`

## Dependencies

- `Microsoft.Agents.Core` — agent runtime
- `Microsoft.Agents.Adapters` — HTTP adapter for proxy communication
- `Microsoft.Agents.Tools.*` — built-in tool packages (file, shell, search)
- `System.Text.Json` — JSON serialization (already a dependency)

## Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Tool schema | MAF provides it | No need to hand-roll 80 dummy tools |
| Tool execution | MAF executes locally | Harness is black box, doesn't know about tools |
| Proxy role | Pure LLM + compression | Compresses whatever MAF sends |
| Metrics source | Control-api (`:8130`) | Existing endpoint, no changes needed |
| Conversation format | Array of prompt strings | Simple, portable, agent-agnostic |
| History management | MAF handles it | Agent framework owns conversation state |

## What This Measures

- **Virtual Tools compression**: Real tool calls with real schemas from MAF
- **Tool result distillation**: Real file contents, shell output, directory listings
- **Working memory compression**: Large tool results from real file reads
- **Tool IR remap**: Real tool call IDs, real remapping
- **Cache alignment**: Repeated file reads, similar prompts
- **Inline wrap-up**: Long conversations with deep tool chains

## Open Questions

- Which MAF NuGet packages are available and stable?
- Does MAF support custom chat endpoints (our proxy)?
- How does MAF handle tool result size limits?
- Do we need to configure MAF's internal tool schema, or is it automatic?
- How does MAF report token usage — can we correlate with proxy metrics?
