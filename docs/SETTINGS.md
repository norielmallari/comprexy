# Settings reference

Operator reference for Comprexy configuration. Structural behavior is described in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Load order

Settings load in order (later sources override earlier ones):

1. `src/Comprexy.Api/appsettings.json`
2. `src/Comprexy.Api/appsettings.{Environment}.json`
3. Optional `src/Comprexy.Api/appsettings.Local.json` (gitignored)
4. User secrets, environment variables, command-line arguments (host defaults)

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

---

## ContextPolicy

Token budgets, compression retain windows, and emergency behavior.

| Key | Default | Description |
| --- | --- | --- |
| `SoftLimitTokens` | `40000` | Above this, background compression is enqueued after a successful reply. |
| `HardLimitTokens` | `52000` | At/above this, send-time retain trim runs; still over → HTTP 413 (unless sync emergency compacts first). |
| `CompressionMaxInputTokens` | `52000` | Max tokens in a compression prompt body. Soft jobs prefer full-raw rebuild when stored messages fit; otherwise merge fold. |
| `EmergencyCompression` | `Off` | `Off` (default): trim then 413. `Sync`: blocking emergency compression when tool chains are closed. |
| `CancelBackgroundCompressionOnChat` | `false` | When `false`, chat waits for in-flight soft compression. When `true`, arriving chat cancels soft compression and continues with last known-good memory. |
| `RetainSelection` | `Fixed` | `Fixed` or `Smart` (soft only). Smart reuses live chat prefix + retain-index instruction. |
| `CompressionRetainMessageCount` | `1` | Fixed soft retain: trailing unfolded messages (atomic assistant+tool groups). `1` = tip only. |
| `EmergencyRecentMessageCount` | `1` | Fixed emergency retain count. |
| `MaxRecentRawTokens` | `24000` | Token budget for Fixed retain window (newest-first). |
| `SmartRetainMaxMessages` | `8` | Smart retain: max messages after clamp. |
| `SmartRetainMaxTokens` | `24000` | Smart retain: max tokens after clamp. |
| `DedupeDuplicateFileReads` | `true` | Soft path: drop older duplicate file-read tool results from compression corpus. |
| `TokenizerEncoding` | `cl100k_base` | Tiktoken encoding for token estimates. |

---

## ToolSchema

Compact tool index for OpenAI-compatible `tools` / `functions` catalogs. Enabled by default; set `Mode` to `Off` to disable. Ignored when `Proxy:PassThrough` is true.

| Key | Default | Description |
| --- | --- | --- |
| `Mode` | `CompactIndex` | `Off` or `CompactIndex`. |
| `MinToolCountToActivate` | `1` | Skip rewrite when the client catalog has fewer tools. |
| `MaxHydrateRoundsPerRequest` | `8` | Caps internal meta-tool + recovery loops per chat request. |
| `SkipRefetchIfHydrated` | `true` | When true, repeat `get_tool_definition` for an already-hydrated tool returns a short `{ "already_hydrated": true, ... }` ack. |
| `InstructionFile` | `Prompts/tool-schema.md` | System rules prepended to the compact index (relative to API content root). |

When `CompactIndex` is active:

- Outbound `tools` is rewritten to `[get_tool_definition]` only.
- A stable system message carries rules + compact index JSON (`name`, `description`, `required` per tool).
- Full schemas are hydrated via proxy-local meta-tool execution and persisted as pinned conversation turns.
- Real tool calls are validated against stored JSON Schema before forwarding to the client.
- If the client catalog already defines `get_tool_definition`, compact index is disabled for that conversation (logged).

See [`ARCHITECTURE.md`](ARCHITECTURE.md#tool-schema-compact-index) for the runtime path.

---

## Auth

| Key | Default | Description |
| --- | --- | --- |
| `RequiredApiKey` | `null` | When set, `/v1/*` requires `Authorization: Bearer {value}` or `X-Api-Key: {value}`. `/health` stays open. |

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
| `Comprexy` | `Data Source=comprexy.db;Cache=Shared` | SQLite database path. WAL and 5s busy timeout are applied on connect. |

Migrations run at startup. Pass `--clear-db` to rebuild from migrations.

---

## Logging

| Key | Default | Description |
| --- | --- | --- |
| `Logging:LogLevel:Default` | `Information` | General log level. |
| `Logging:LogLevel:Comprexy` | `Information` | Comprexy application logs. Set to `Trace` to enable payload trace categories. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | ASP.NET Core logs. |
| `Logging:LogLevel:Microsoft.EntityFrameworkCore` | `Warning` | EF Core logs. |
