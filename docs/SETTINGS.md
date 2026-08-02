# Settings reference

Operator reference for Comprexy configuration. Structural behavior is described in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Load order

Settings load in order (later sources override earlier ones):

1. `apps/proxy/appsettings.json` (proxy) or `apps/control-api/appsettings.json` (control-api)
2. `apps/*/appsettings.{Environment}.json`
3. User secrets, environment variables, command-line arguments (from `WebApplication.CreateBuilder`)
4. Shared default: hosts rewrite `ConnectionStrings:Comprexy` to `data/comprexy.db` under the repo root (`SharedSqliteConfiguration.UseRepoSharedDatabase`)
5. Optional `apps/*/appsettings.Local.json` (gitignored) — may override connection string and other settings
6. Environment variables and command-line arguments again (re-appended by both hosts)

Both hosts re-append the environment-variable and command-line providers after the SharedSqlite rewrite and `appsettings.Local.json`, so an env override such as `ConnectionStrings__Comprexy` or `ToolSchema__Mode` wins over both. Local.json remains the convenient place for machine-local defaults; env/cmdline is what a harness or container uses to override them for one process.

Defaults in the tables below match stock `apps/proxy/appsettings.json` (and control-api where noted). C# `*Options` property initializers may differ when a key is omitted entirely.

Copy `appsettings.Local.json.example` → `appsettings.Local.json` for machine-local upstream URL, API keys, and audit logging.

## Conversation identity

Send a unique `X-Comprexy-Conversation-Id` header per logical session when multiple clients or tabs might share the same opening prompt.

When omitted, Comprexy fingerprints the system prompt plus the first two **plain** user turns (Cursor `<user_query>` text when present; tool-echo user turns like `Called the … tool with the following input:` are skipped). Templated openings can still collide across sessions. The resolved conversation id is echoed on responses.

---

## Provider

Upstream OpenAI-compatible chat endpoint.

| Key | Default | Description |
| --- | --- | --- |
| `Type` | `OpenAICompatible` | Provider kind. Only `OpenAICompatible` is supported. |
| `BaseUrl` | `http://localhost:11434/v1` | Upstream `/v1` base URL. |
| `ApiKey` | `null` | Optional Bearer token. When null/empty, no `Authorization` header is sent. |
| `Model` | `null` | When set, replaces the client `model` on outbound chat/compression calls. When null, the client's `model` is forwarded. |
| `TimeoutSeconds` | `600` | Per-request timeout for chat completion calls. |

---

## Compression

Optional separate endpoint/model for ToolSchema mapping and Inline wrap-up prompt files. Unset BaseUrl/ApiKey/Model/Timeout fall back to `Provider`. Inline wrap-up itself uses the live chat endpoint; these knobs still drive the ToolSchema mapper.

| Key | Default | Description |
| --- | --- | --- |
| `BaseUrl` | `null` | Compression endpoint (ToolSchema mapper). Falls back to `Provider:BaseUrl`. |
| `ApiKey` | `null` | Compression API key. Falls back to `Provider:ApiKey`. |
| `Model` | `null` | Compression model. Falls back to `Provider:Model`, then the client chat model. |
| `TimeoutSeconds` | `600` | Compression-endpoint timeout. When null/omitted, falls back to `Provider:TimeoutSeconds`. |
| `Temperature` | `0.6` | Sampling temperature for Compression-endpoint calls. The Virtual Tools mapper runs its first attempt at `0` and widens from this value on each retry. |
| `EnableThinking` | `false` | When false, sends `chat_template_kwargs.enable_thinking=false` on Compression-endpoint calls. |
| `InlineInstructionFile` | `Prompts/compression-inline.md` | Inline follow-up wrap-up **user** prompt (return-only WM). |
| `WorkingMemoryTemplateFile` | `Prompts/working-memory-template.md` | Shared `# Working Memory` markdown skeleton appended to Inline wrap-up prompts. |

---

## ContextPolicy

Soft token budget and the Inline fold retain window.

| Key | Default | Description |
| --- | --- | --- |
| `SoftLimitTokens` | `32000` | Above this after a successful reply: Inline follow-up wrap-up on eligible turns (closed stored tool chain + cooldown). |
| `MinTurnsBetweenGenerations` | `6` | Assistant turns after a successful Inline generation before another follow-up wrap-up. |
| `CompressionRetainMessageCount` | `1` | Inline fold retain window: trailing unfolded messages kept raw, newest-first. Atomic assistant+tool groups count as one unit and the newest group is kept whole even if larger. `1` = tip only. |
| `DedupeDuplicateFailedEdits` | `true` | Live chat: wire-only omit older identical failed file-edit tool results (path + `old_string` last-wins) so StrReplace failure loops do not stack. Applied to the retain window (baked into the Cache Alignment Prefix), never to the tip. |
| `TokenizerEncoding` | `cl100k_base` | Tiktoken encoding for token estimates. |

Inline wrap-up reuses live sampling / `chat_template_*` and keeps the live `tools` / `functions` catalog for provider KV alignment; it sets `tool_choice` / `function_call` to `none` so wrap-up cannot continue the agent loop. A wrap-up that still returns `tool_calls` soft-fails as `wrapup_tool_calls`. Soft-pressure eligible turns hold the streaming tail until wrap-up finishes (success or soft-fail): `[DONE]` on stop turns, and the whole real `tool_calls` tail on mid-chain turns so the client starts tools only after the checkpoint attempt resolves. When unfolded history still has failed edits on a path, Inline fold **pins** the last successful mutation atomic group for that path into the retain set (in addition to `CompressionRetainMessageCount`) so the post-edit tip is not erased.

---

## CacheAlignment

Process-local wrap-up-ready message Prefix for provider KV / prompt-cache alignment. Registered only when proxy services are enabled (`enableProxyServices: true`). Not a SQLite / TTL conversation-row cache.

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | When true, prepare/wrap-up use `ICacheAlignmentService`. When false, prepare uses `ContextBuilder.Build` every turn. |
| `MaxConversations` | `1024` | Max conversations in the process-local Prefix map (entry weight = 1). Evicts least-recently-used when over cap. |

---

## ToolSchema

Virtual Tools (Tool IR) is a primary Comprexy capability for OpenAI-compatible `tools` / `functions` catalogs. It is enabled by default (`Mode: Virtual`); set `Mode` to `Off` to disable. Ignored when `Proxy:PassThrough` is true. Structural runtime path: [`ARCHITECTURE.md`](ARCHITECTURE.md#tool-schema-virtual-tools).

| Key | Default | Description |
| --- | --- | --- |
| `Mode` | `Virtual` | `Off` or `Virtual`. |
| `MappingMaxRetries` | `2` | Extra mapper attempts after the first on invalid MappingJson (total attempts = 1 + this value). Invalid maps are never persisted. After the last attempt, bindings that failed validation are dropped and the rest are kept; Tool IR is disabled for the hash only when nothing usable survives. |
| `MaxRangeLines` | `250` | Cap for `comprexy_read_file_range` observations (`truncated: true` when capped). |
| `MaxSearchMatches` | `40` | Cap for `comprexy_read_file_search` hits. |
| `MaxDirListEntries` | `200` | Cap for `comprexy_dir_list` entries. |
| `MaxShellObservationChars` | `4000` | Cap for distilled `comprexy_shell` observation content (`truncated: true` when capped). |
| `ExcludeFromModelTools` | `ReadLints`, `TodoWrite`, `AwaitShell`, `UpdateCurrentStep`, `EditNotebook`, `SwitchMode`, `agent_manager`, `agent_manager_models`, `background_process`, `kilo_local_recall` | Client tool names omitted from the model-facing `tools[]` when Virtual is active (case-insensitive ordinal match after trim). Still hashed/mapped as part of the inbound catalog; model calls are rejected locally; inbound orphans are swallowed like Virtual-replaced tools. Empty list disables. Ignored when `Mode=Off` or `Proxy:PassThrough`. |
| `FileCacheAbsoluteExpiration` | `00:20:00` | TTL for in-memory file-body cache entries. |
| `FileCacheSizeLimit` | `256` | Max cached file bodies (each entry size 1). |
| `CallIdMapPendingAbsoluteExpiration` | `00:30:00` | TTL for abandoned pending IR↔client call-id map rows (EF + in-memory hot cache). |
| `CallIdMapMaxConversations` | `1024` | Max conversations retained in the process-local call-id hot cache. |

When `Virtual` is active:

- On catalog hash miss, Comprexy calls the **Compression** endpoint to produce validated **MappingJson** (blocking), using `Compression:Model` → `Provider:Model` → the client chat `model` from the request. Bindings must cover every client-schema `required` property via `arg_map` or `defaults`. Mapper prompt+completion tokens are added to conversation `TotalCompressionOverheadTokens`. Failures after retries set `ToolIrDisabled` for that hash and forward client tools unchanged; compression/budgets stay on.
- Outbound `tools` = bound Virtual registry tools (`comprexy_read_file_*` / `comprexy_dir_list` / `comprexy_shell`) + `comprexy_get_current_conversation_id` + full-schema client tools that are not replaced and not listed in `ExcludeFromModelTools`.
- The deterministic planner remaps IR calls to native client tool names/args via `arg_map` + `defaults` (or satisfies from the file-body cache). Stream and non-stream responses both rewrite `tool_calls` toward the client. Pending dual-id mappings are persisted in `ConversationToolCallMaps` (committed before emit) with an in-memory hot cache; TTL still applies to abandoned pending rows.
- Inbound native tool results are distilled into IR observations for the model-facing transcript (map lookup: memory, else SQLite).
- If the client catalog already defines a reserved name (`comprexy_get_current_conversation_id` or any Virtual `comprexy_*` IR tool name), Virtual is disabled for that conversation (logged).

---

## Auth

| Key | Default | Description |
| --- | --- | --- |
| `RequiredApiKey` | `null` | When set, `/v1/*` and control-api `/mcp` require `Authorization: Bearer {value}` or `X-Api-Key: {value}`. `/health` stays open. |

When unset, those routes accept any (or no) credential. For non-loopback deployments, set a key and prefer HTTPS.

Proxy stock `AllowedHosts` is `*` (`apps/proxy/appsettings.json`). control-api secure host defaults (override via environment / `appsettings.Local.json` for remote hostnames):

| Key | Default | Description |
| --- | --- | --- |
| `AllowedHosts` | `localhost;127.0.0.1` | ASP.NET Core host filtering; rejects other `Host` headers. |
| `Cors:AllowedOrigins` | `[]` (empty) | When empty, the default CORS policy denies all browser origins. List explicit origins to allow browser clients; do not use `*`. |

Server-side MCP clients (Cursor, etc.) do not rely on CORS. Wildcard `AllowedHosts: "*"` is not the control-api default. The optional metrics dashboard (`apps/dashboard`, `:3000`) is a browser client of control-api REST — Development already allows `http://localhost:3000`; for other hosts, list the origin in `Cors:AllowedOrigins`.

---

## Proxy

| Key | Default | Description |
| --- | --- | --- |
| `PassThrough` | `false` | When true, forwards the original chat body with no context rebuild, compression / working memory, or Virtual Tools rewrite. Escape hatch only. |
| `StripReasoningContent` | `false` | When true, strips `reasoning_content` / `reasoning` from outbound chat and compression messages. |

---

## Metrics

Token proof ledger for successful compressed-path turns. Persisted in SQLite (not Trace logs).

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | When true, records per-turn raw vs compressed token metrics and folds Inline wrap-up and Tool IR schema-mapping LLM usage into conversation summaries. |

Operator read API (same `/v1/*` API-key gate as chat):

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/v1/comprexy/conversations` | List conversation metric summaries |
| `GET` | `/v1/comprexy/conversations/{conversationId}/metrics` | Conversation rollup |
| `GET` | `/v1/comprexy/conversations/{conversationId}/metrics/turns` | Per-turn breakdown |

Pass-through turns and failed requests do not write turn metrics. See [`ARCHITECTURE.md`](ARCHITECTURE.md) and the internal metrics plan for formulas.

---

## McpTelemetry

control-api only. Bounds and timeouts for the remote telemetry + retrieval MCP endpoint (`/mcp`, Streamable HTTP, stateless). REST metrics and MCP share `IConversationMetricsQueryService`; message/working-memory RAG tools share `IConversationRetrievalQueryService`. MCP does not open a separate database path.

| Key | Default | Description |
| --- | --- | --- |
| `DefaultRowLimit` | `100` | Default max turns/messages returned by bounded telemetry and retrieval projections (phases, turns, timeline, message windows, search). |
| `MaxRowLimit` | `1000` | Hard cap applied before EF `Take(...)`. |
| `QueryTimeoutSeconds` | `5` | Linked cancellation timeout for each MCP telemetry/retrieval query. |

Telemetry summary semantics: `TurnCount`, weighted savings, simple average, peak, and final-turn fields are whole-conversation (rollup + final-turn query + EF aggregates). `NetTokensSaved` / savings ratios compare tiktoken **raw client** vs **prepared upstream** estimates only; `ActualPromptTokens` and `PromptEstimateError` measure estimate accuracy against upstream `usage.prompt_tokens` and do not enter the savings numerator. `MedianSavingsRatio` and `SavingsRegressions` use the bounded `TurnIndex`-ordered sample only; when `IsPartialTurnSample` is true, those sample fields cover `SampleFirstTurnIndex`–`SampleLastTurnIndex` (`SampleTurnCount` turns), not the full conversation.

Retrieval tools (keyword search, sequence windows, recent messages, working-memory snapshot, open tool chains) read `ConversationMessage` / `WorkingMemory` with snippet truncation (500 chars content / 4096 chars optional wire JSON). Message tools use `Sequence`; do not conflate with telemetry `TurnIndex`. Open tool chains reuse `ToolCallChainState` over unfolded messages.

Local MCP URL: `http://localhost:8130/mcp`. Any IDE, coding agent, or MCP client with remote Streamable HTTP support can connect to it. When `Auth:RequiredApiKey` is set, send the same Bearer / `X-Api-Key` credentials used for `/v1`. All telemetry tools are named `comprexy_*` and require `conversationId` (from the proxy meta-tool `comprexy_get_current_conversation_id`, operator tooling, or response header `X-Comprexy-Conversation-Id`). Resources use `comprexy://conversation/{conversationId}/…`. Metric DTOs omit `RequestHash` / `SentPayloadHash`.

---

## Trace

Console payload categories require `Logging:LogLevel:Comprexy` = `Trace`. `RequestFiles` writes audit files regardless of console toggles.

| Key | Default | Description |
| --- | --- | --- |
| `ClientInput` | `false` | Console: raw client request. |
| `ClientOutput` | `false` | Console: client response (streaming: reassembled). |
| `ModelInput` | `false` | Console: upstream chat request. |
| `ModelOutput` | `false` | Console: upstream chat response. |
| `CompressionModelInput` | `false` | Console: compression request. |
| `CompressionModelOutput` | `false` | Console: compression response. |
| `ContextBudget` | `false` | Console: token estimates and budget decisions. |
| `RequestFiles` | `false` | Write full audit files under `RequestLogDirectory`. |
| `RequestLogDirectory` | `logs/requests` | Audit file directory (relative to API content root). |
| `MaxPayloadChars` | `32768` | Truncate logged payloads. `0` = no truncation. |

Payload formatting is for human reading: relaxed JSON escaping (literal `` ` `` / `>` instead of `\u0060` / `\u003E`), multiline `content` / `reasoning*` as `|` blocks, and nested JSON in tool `arguments` expanded when parseable. Audit files are not meant to be machine-round-tripped.

**Development defaults:** quiet console (`Comprexy` = `Information`), `RequestFiles: false`.

---

## Comprexy:TokenEstimateCache

In-memory cache for tiktoken estimates.

| Key | Default | Description |
| --- | --- | --- |
| `AbsoluteExpiration` | `00:15:00` | Cache entry lifetime. |
| `SizeLimit` | `10000` | Max cached estimates. |

---

## ConnectionStrings

| Key | Default | Description |
| --- | --- | --- |
| `Comprexy` | rewritten to `data/comprexy.db` under repo root | SQLite database path. Hosts call `SharedSqliteConfiguration.UseRepoSharedDatabase`, then re-append env/cmdline, so `ConnectionStrings__Comprexy` or `appsettings.Local.json` both override the shared default. WAL and 5s busy timeout are applied on connect. |

Migrations run at startup. Pass `--clear-db` to rebuild from migrations.

---

## Logging

| Key | Default | Description |
| --- | --- | --- |
| `Logging:LogLevel:Default` | `Information` | General log level. |
| `Logging:LogLevel:Comprexy` | `Information` | Comprexy application logs. Set to `Trace` to enable payload trace categories. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | ASP.NET Core logs. |
| `Logging:LogLevel:Microsoft.EntityFrameworkCore` | `Warning` | EF Core logs. |
