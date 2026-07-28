# Settings reference

Operator reference for Comprexy configuration. Structural behavior is described in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Load order

Settings load in order (later sources override earlier ones):

1. `apps/proxy/appsettings.json` (proxy) or `apps/control-api/appsettings.json` (control-api)
2. `apps/*/appsettings.{Environment}.json`
3. Shared default: hosts rewrite `ConnectionStrings:Comprexy` to `data/comprexy.db` under the repo root
4. Optional `apps/*/appsettings.Local.json` (gitignored) — may override connection string and other settings
5. User secrets, environment variables, command-line arguments (host defaults)

Copy `appsettings.Local.json.example` → `appsettings.Local.json` for machine-local upstream URL, API keys, and audit logging.

## Conversation identity

Send a unique `X-Comprexy-Conversation-Id` header per logical session when multiple clients or tabs might share the same opening prompt.

When omitted, Comprexy fingerprints the system prompt plus the first two user message texts. Templated openings can still collide across sessions. The resolved conversation id is echoed on responses.

---

## Provider

Upstream OpenAI-compatible chat endpoint.

| Key | Default | Description |
| --- | --- | --- |
| `Type` | `OpenAICompatible` | Provider kind. Only `OpenAICompatible` is supported. |
| `BaseUrl` | `http://localhost:11434/v1` | Upstream `/v1` base URL. |
| `ApiKey` | `null` | Optional Bearer token. When null/empty, no `Authorization` header is sent. |
| `Model` | `null` | When set, replaces the client `model` on outbound chat/compression calls. When null, the client's `model` is forwarded. |
| `TimeoutSeconds` | `120` | Per-request timeout for chat completion calls. |

---

## Compression

Optional separate endpoint/model for LLM-based context compression. Unset fields fall back to `Provider`.

| Key | Default | Description |
| --- | --- | --- |
| `BaseUrl` | `null` | Compression endpoint. Falls back to `Provider:BaseUrl`. |
| `ApiKey` | `null` | Compression API key. Falls back to `Provider:ApiKey`. |
| `Model` | `null` | Compression model. Falls back to `Provider:Model`, then the client chat model from the triggering turn. |
| `TimeoutSeconds` | `null` | Compression timeout. Falls back to `Provider:TimeoutSeconds`. Prefer a generous value for local models (default in appsettings is 600). |
| `Temperature` | `0.2` | Sampling temperature for compression calls. |
| `EnableThinking` | `false` | When false, sends `chat_template_kwargs.enable_thinking=false` on compression calls. |
| `InstructionFile` | `Prompts/compression-fixed.md` | Fixed compression system prompt (relative to API content root). |
| `SmartInstructionFile` | `Prompts/compression-smart.md` | Smart compression trailing user instruction. |
| `InlineInstructionFile` | `Prompts/compression-inline.md` | Inline follow-up wrap-up **user** prompt (return-only WM) when `RetainSelection=Inline`. |
| `WorkingMemoryTemplateFile` | `Prompts/working-memory-template.md` | Shared `# Working Memory` markdown skeleton appended to Fixed, Smart, and Inline wrap-up prompts. |

---

## ContextPolicy

Token budgets, compression retain windows, and emergency behavior.

| Key | Default | Description |
| --- | --- | --- |
| `SoftLimitTokens` | `40000` | Above this, background compression is enqueued after a successful reply. |
| `HardLimitTokens` | `64000` | At/above this, send-time retain trim runs; still over → HTTP 413 (unless sync emergency compacts first). Set to `null` to disable hard-limit checking (no emergency trim / 413). |
| `CompressionMaxInputTokens` | `65536` | Max tokens in a compression prompt body. Soft jobs prefer full-raw rebuild when stored messages fit; otherwise merge fold. Set to `null` to disable the compression input cap (soft compression then always prefers full-raw rebuild; compression still runs). |
| `EmergencyCompression` | `Off` | `Off` (default): trim then 413. `Sync`: blocking emergency compression when tool chains are closed. Ignored when `RetainSelection=Inline` (hard path is trim then 413). |
| `CancelBackgroundCompressionOnChat` | `false` | When `false`, chat waits for in-flight soft compression. When `true`, arriving chat cancels soft compression and continues with last known-good memory. |
| `RetainSelection` | `Inline` | `Inline` (default), `Fixed`, or `Smart` (soft only). Inline disables background soft jobs and emergency sync; after an eligible soft-pressure visible answer, a blocking proxy-internal wrap-up produces working memory (including mid-chain tool hops that open a new chain after a closed stored prefix). Wrap-up reuses live sampling / `chat_template_*` but omits tool-calling fields (`tools`, `tool_choice`, `functions`, and related `function_call` / `parallel_tool_calls` when present); a wrap-up that still returns `tool_calls` soft-fails as `wrapup_tool_calls`. Soft-pressure eligible turns hold the streaming tail until wrap-up finishes (success or soft-fail): `[DONE]` on stop turns, and the whole real `tool_calls` tail on mid-chain turns so the client starts tools only after the checkpoint attempt resolves. Tool-marathon hops can therefore add wrap-up latency under soft pressure + cooldown. Smart reuses live chat prefix + retain-index instruction. Fixed uses trailing retain window. Emergency Sync still uses Fixed-style compact when `RetainSelection` is Fixed/Smart (see TODO-013 for Inline emergency). |
| `MinTurnsBetweenGenerations` | `6` | Inline only: assistant turns after a successful Inline generation before another follow-up wrap-up. Ignored by Fixed/Smart. |
| `CompressionRetainMessageCount` | `1` | Fixed soft retain: trailing unfolded messages (atomic assistant+tool groups). `1` = tip only. |
| `EmergencyRecentMessageCount` | `1` | Fixed emergency retain count. |
| `MaxRecentRawTokens` | `24000` | Token budget for Fixed retain window (newest-first). |
| `SmartRetainMaxMessages` | `8` | Smart retain: max messages after clamp. |
| `SmartRetainMaxTokens` | `24000` | Smart retain: max tokens after clamp. |
| `DedupeDuplicateFileReads` | `true` | Soft path: drop older duplicate file-read tool results from the compression corpus (then fold). Live chat: wire-only omit older same-path reads from the outgoing retain window so Read loops do not stack identical tool results. |
| `TokenizerEncoding` | `cl100k_base` | Tiktoken encoding for token estimates. |

---

## ToolSchema

Compact tool index for OpenAI-compatible `tools` / `functions` catalogs. Enabled by default; set `Mode` to `Off` to disable. Ignored when `Proxy:PassThrough` is true.

| Key | Default | Description |
| --- | --- | --- |
| `Mode` | `CompactIndex` | `Off` or `CompactIndex`. |
| `MinToolCountToActivate` | `1` | Skip rewrite when the client catalog has fewer tools. |
| `MaxHydrateRoundsPerRequest` | `8` | Caps internal meta-tool + recovery loops per chat request. Exhaustion returns `finish_reason=stop` text (never forwards `get_tool_definition` to the client). |
| `SkipRefetchIfHydrated` | `true` | When true, repeat `get_tool_definition` for an already-hydrated tool skips re-marking hydration and returns `{ "already_hydrated": true, "tool_name", "instruction", "definition" }` (definition is always included; instruction tells the model to emit that exact function name, not nest it under `CallMcpTool.toolName`). Hydrated tools from this request's loop are added to outbound `tools[]` on the next meta round. A second consecutive already-hydrated-only round stops the hydrate loop early. |
| `InstructionFile` | `Prompts/tool-schema.md` | System rules prepended to the compact index (relative to API content root). |

When `CompactIndex` is active:

- Outbound `tools` is rewritten to `[get_tool_definition, get_current_conversation_id]`.
- A stable system message carries rules + compact index JSON (`name`, `description`, `required` per tool).
- Full schemas are hydrated via proxy-local meta-tool execution and persisted as pinned conversation turns.
- `get_current_conversation_id` returns the active session UUID for tools that require `conversationId`.
- Real tool calls are validated against stored JSON Schema before forwarding to the client.
- If the client catalog already defines a reserved meta tool (`get_tool_definition` or `get_current_conversation_id`), compact index is disabled for that conversation (logged).

See [`ARCHITECTURE.md`](ARCHITECTURE.md#tool-schema-compact-index) for the runtime path.

---

## Auth

| Key | Default | Description |
| --- | --- | --- |
| `RequiredApiKey` | `null` | When set, `/v1/*` and control-api `/mcp` require `Authorization: Bearer {value}` or `X-Api-Key: {value}`. `/health` stays open. |

When unset, those routes accept any (or no) credential. For non-loopback deployments, set a key and prefer HTTPS.

control-api secure host defaults (override via environment / `appsettings.Local.json` for remote hostnames):

| Key | Default | Description |
| --- | --- | --- |
| `AllowedHosts` | `localhost;127.0.0.1` | ASP.NET Core host filtering; rejects other `Host` headers. |
| `Cors:AllowedOrigins` | `[]` (empty) | When empty, the default CORS policy denies all browser origins. List explicit origins to allow browser clients; do not use `*`. |

Server-side MCP clients (Cursor, etc.) do not rely on CORS. Wildcard `AllowedHosts: "*"` is not the control-api default.

---

## Proxy

| Key | Default | Description |
| --- | --- | --- |
| `PassThrough` | `false` | When true, forwards the original chat body without rebuild, compression, hard-limit 413, or tool-schema rewrite. Escape hatch only. |
| `StripReasoningContent` | `false` | When true, strips `reasoning_content` / `reasoning` from outbound chat and compression messages. |

---

## Metrics

Token proof ledger for successful compressed-path turns. Persisted in SQLite (not Trace logs).

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | When true, records per-turn raw vs compressed token metrics and folds compression LLM usage into conversation summaries. |

Operator read API (same `/v1/*` API-key gate as chat):

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/v1/comprexy/conversations` | List conversation metric summaries |
| `GET` | `/v1/comprexy/conversations/{conversationId}/metrics` | Conversation rollup |
| `GET` | `/v1/comprexy/conversations/{conversationId}/metrics/turns` | Per-turn breakdown |

Pass-through turns and failed/413 requests do not write turn metrics. See [`ARCHITECTURE.md`](ARCHITECTURE.md) and the internal metrics plan for formulas.

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

Local MCP URL: `http://localhost:8130/mcp`. Any IDE, coding agent, or MCP client with remote Streamable HTTP support can connect to it. When `Auth:RequiredApiKey` is set, send the same Bearer / `X-Api-Key` credentials used for `/v1`. Argument-free `get_current_*` tools and `comprexy://current/*` resources require `X-Comprexy-Conversation-Id` on the MCP HTTP request; clients that cannot forward that header should call explicit tools with `conversationId` set to the UUID from the proxy meta-tool `get_current_conversation_id` (or from operator tooling / response header `X-Comprexy-Conversation-Id`). Metric DTOs omit `RequestHash` / `SentPayloadHash`.

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
| `Comprexy` | rewritten to `data/comprexy.db` under repo root | SQLite database path. Hosts call `SharedSqliteConfiguration.UseRepoSharedDatabase`; override via Local.json / env. WAL and 5s busy timeout are applied on connect. |

Migrations run at startup. Pass `--clear-db` to rebuild from migrations.

---

## Logging

| Key | Default | Description |
| --- | --- | --- |
| `Logging:LogLevel:Default` | `Information` | General log level. |
| `Logging:LogLevel:Comprexy` | `Information` | Comprexy application logs. Set to `Trace` to enable payload trace categories. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | ASP.NET Core logs. |
| `Logging:LogLevel:Microsoft.EntityFrameworkCore` | `Warning` | EF Core logs. |
