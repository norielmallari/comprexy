# Metrics: parent + subagent conversation rollup

Research and implementation plan for including spawned subagent sessions in workflow-level metrics. Backlog: [TODO-013](../TODO.md#todo-013--metrics-rollup-for-subagent-conversations).

## Problem

Comprexy metrics and evidence are scoped to a single `ConversationId`. Cursor Task / cloud subagents (and similar orchestration) open **separate** chat sessions against the proxy. Each session gets its own `Conversation`, working memory, tool catalog, and `ConversationMetricsSummary`.

Parent-session totals therefore **undercount** full workflow token use. Evidence already documents this for the dashboard run (`docs/evidence/5ca87ca.md`): parent-only telemetry; subagent tokens excluded.

This is an important operator metric: orchestration-heavy workflows spend a large share of tokens in children, not only in the parent loop.

## Current state (code)

### Identity — no parent link

| Fact | Where |
| --- | --- |
| Identity is either `X-Comprexy-Conversation-Id` → `header:{id}` or fingerprint of system + first two plain user turns | `ConversationIdentityResolver` |
| Lookup/create is by `ConversationKey` only; new rows get a fresh `Guid Id` | `ProxyChatCompletionService.PrepareAsync`, `Conversation.Create` |
| `Conversation` fields: `ConversationKey`, `SystemPrompt`, `SyncedMessageCount`, timestamps — **no** `ParentConversationId` | `Conversation.cs`, EF snapshot |
| Only Comprexy chat header today | `ComprexyHeaders.ConversationId` (`X-Comprexy-Conversation-Id`) |
| Response header returns **entity** `Conversation.Id`, not the raw client key | `ChatCompletionEndpoints` |

Subagent sessions almost always diverge on fingerprint inputs (specialist system prompt + unique Task prompt), so they become **separate conversations** by design. That is correct for compression isolation; it is wrong for workflow-level proof if left unlinked.

### Metrics — single conversation only

| Path | Behavior |
| --- | --- |
| Write | `ConversationMetricsRecorder` keys turns/summary on one `ConversationId` |
| REST | `GET …/conversations`, `…/{id}/metrics`, `…/{id}/metrics/turns` — flat / single id (`MetricsEndpoints`) |
| MCP | All tools take one `conversationId`; `comprexy_compare_conversations` is **side-by-side**, not a summed tree |
| Dashboard | Flat selector (`useConversations` → list endpoint); metrics/turns for the selected id only |

Pass-through and non-proxied paths (e.g. some cloud runners) produce **no** Comprexy turn metrics at all; linking cannot invent those numbers.

### Existing hooks (not linking)

- `comprexy_get_current_conversation_id` returns **this** session’s UUID for telemetry MCP — parents can embed it in Task prompts, but Comprexy does not persist a parent pointer from that.
- Task tool calls may appear in the parent transcript; nothing parses them for child Comprexy ids.
- `comprexy_search_conversation` is single-conversation substring search — usable for fragile UUID hunting, not a durable link.
- Exclusive gate is per `ConversationKey`; parent and child correctly run in parallel under different keys.

## Design principles

1. **Keep `Conversation` as the unit of compression** — separate WM, tool catalogs, dual-id maps, and gates per session. Do **not** merge child transcripts into the parent.
2. **Linking is metadata** — optional parent pointer on the child row (or equivalent).
3. **Rollup is a query concern** — sum/recompute from existing per-conversation summaries and turn rows; do not mutate the parent’s `ConversationMetricsSummary` to absorb children.
4. **Prefer explicit client signals** over heuristics (data-integrity guardrails).

## Recommended approach

### Ingest: parent header + durable FK

1. Add optional request header `X-Comprexy-Parent-Conversation-Id` (value = parent’s entity `Conversation.Id` GUID from a prior response header or meta-tool).
2. Add nullable `Guid? ParentConversationId` on `Conversation`, set on **first create** when the header resolves to an existing conversation.
3. Index `(ParentConversationId)` for child listing.
4. Optionally continue to encourage a stable `X-Comprexy-Conversation-Id` **per child session** so multi-turn subagents do not rely on fingerprint alone.

```text
Parent chat → Conversation P (Id = …)
Subagent chat → header Parent = P.Id → Conversation C with ParentConversationId = P.Id
Query tree(P) → { P } ∪ children(P) → rolled-up DTO
```

### Query: tree / rollup API

Add Application facade methods (e.g. on `IConversationMetricsQueryService`) that:

- List children for a parent id
- Return parent summary + child summaries + **rollup** totals
- Optionally return a merged turn timeline tagged by source `conversationId` (order by request time — `TurnIndex` is not comparable across conversations)

Expose via:

| Surface | Suggested |
| --- | --- |
| REST | `GET /v1/comprexy/conversations/{id}/metrics/tree` (or `/family`) |
| MCP | `comprexy_get_conversation_tree_metrics` |
| Dashboard | Rollup toggle + grouped selector (parent with indented children) |

### Rollup field semantics (proposed defaults)

| Field | Rollup |
| --- | --- |
| Baseline / compressed / net saved / overhead token totals | **Sum** across `{parent} ∪ children` |
| Weighted savings ratio | Recompute from summed baseline and summed net saved |
| Simple / median ratios, peak turn, final-turn snapshot | Document as **parent-only** or **annotated per member** — do not pretend a single “final turn” for the tree without labeling source |
| Turn charts | Union of turns with `conversationId` (and optional role: parent/child) |

Document clearly in SETTINGS / MCP descriptions that single-conversation tools remain single-conversation; tree tools are the workflow view.

## Alternatives (ranked)

| Approach | Fit | Notes |
| --- | --- | --- |
| **A+B — parent header + `ParentConversationId` + query rollup** | Best | Explicit, durable, architecture-aligned |
| Operator manual / compare MCP | Interim | Works today for two ids; no automation |
| Embed parent UUID in Task prompt + search | Fragile | Depends on prompt discipline; no FK |
| Time-window / system-prompt heuristics | Poor as primary | False positives; no ground truth |
| Merge sessions into one conversation | Rejected | Breaks WM, catalogs, gates, identity |

## Out of scope / do not

- Folding child messages into the parent transcript or shared `ConversationKey`
- Overwriting parent summary rows with tree totals
- Claiming cloud / non-proxied subagent tokens without a Comprexy path
- Heuristic-only production linking without an explicit signal or operator attach path

## Implementation sketch

### Schema / domain

- `Conversation.ParentConversationId` (`Guid?`) + setter on create (immutable after set, or allow operator re-parent later — decide in implementation)
- EF configuration + index; migration via `dotnet ef`
- `IConversationRepository.ListByParentIdAsync`

### Proxy

- `ComprexyHeaders.ParentConversationId`
- Parse into `IncomingChatRequest`
- On create in `PrepareAsync`: resolve parent by `Id`; if missing, fail closed or ignore with visible log — **decide** (prefer fail closed when header present but invalid)

### Application / control-api / dashboard

- Tree DTO + query methods
- REST + MCP tool (+ optional resource template)
- Dashboard API client, selector grouping, rollup metrics cards / chart series

### Docs

- `docs/ARCHITECTURE.md` — conversation family metadata; rollup as control-plane query
- `docs/SETTINGS.md` — parent header; tree endpoint / MCP tool
- Evidence templates — note when figures are tree vs parent-only

### Client / orchestration dependency

Rollup only fills in when something sets the parent header on subagent requests (Cursor config, local proxy wrapper, or SDK). Until that exists, schema + APIs still enable **operator attach** (manual `ParentConversationId` update) as a follow-up.

## Open questions

1. Can Cursor (or a local wrapper) set custom HTTP headers on Task/subagent `/v1/chat/completions` calls?
2. Fail closed vs soft-ignore when `X-Comprexy-Parent-Conversation-Id` does not match an existing id?
3. One-level parent only, or nested trees (subagent spawning subagent)?
4. Retroactive operator linking UI/API for orphan children?
5. Double-counting: parent baseline already includes Task tool **payloads** in the parent transcript; child sessions are **additional** upstream chats — tree sum is the right “tokens actually sent to the model across sessions,” but evidence copy must say so.
6. Tenant/auth: when multi-key lands ([TODO-005](../TODO.md#todo-005--multi-key--per-tenant-api-key-management)), restrict parent links to same tenant.

## Acceptance criteria (for the TODO)

- [ ] Optional parent link persisted on child `Conversation` from an explicit client signal (header and/or operator attach).
- [ ] Query API returns parent + children + summed token/savings rollup without merging transcripts.
- [ ] REST and MCP expose the tree/rollup; single-conversation tools unchanged in semantics.
- [ ] Dashboard can show workflow rollup for a parent with linked children (or document deferred UI if API-first).
- [ ] Architecture / SETTINGS / evidence guidance updated; tests for link ingest + rollup math.
- [ ] Documented limitation: unproxied / pass-through children remain invisible to Comprexy metrics.

## References

- Evidence coverage note: [`docs/evidence/5ca87ca.md`](../evidence/5ca87ca.md)
- Identity / metrics ownership: [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md)
- Metrics REST / MCP bounds: [`docs/SETTINGS.md`](../SETTINGS.md)
- Compare (non-rollup): `comprexy_compare_conversations` in `apps/control-api/Mcp/Tools/ConversationTools.cs`
