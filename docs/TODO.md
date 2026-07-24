# Backlog

Deferred and planned work for Comprexy. Prefer [GitHub Issues](https://github.com/norielmallari/comprexy/issues) when an item needs discussion, assignees, or cross-PR tracking.

| Status | Meaning |
| --- | --- |
| `open` | Ready to implement |
| `deferred` | Accepted for now; documented workaround or lower priority |
| `done` | Finished — leave a short note, then archive or remove |

| Priority | Meaning |
| --- | --- |
| High | Affects correctness or multi-session safety for common setups |
| Medium | Improves durability, shared deploy, or operator clarity |
| Low | Latent or low-impact until related code paths change |

---

## TODO-001 — Stronger conversation identity

| Field | Value |
| --- | --- |
| **Status** | `deferred` |
| **Priority** | High |
| **Area** | `ConversationIdentityResolver`, conversation keying |

**Summary:** When `X-Comprexy-Conversation-Id` is omitted, identity is derived from the system prompt and first two user message texts only. Sessions that share the same opening text can map to one stored conversation.

**Workaround:** Send a unique `X-Comprexy-Conversation-Id` per logical session (recommended for multi-tab and multi-user setups). See the [README limitations](../README.md#limitations).

**Acceptance criteria:**

- [ ] Fingerprint (or successor) incorporates more than plain text — for example normalized wire JSON and/or non-text parts — **or** fingerprint-only mode is clearly unsuitable by default for shared deployments.
- [ ] Documented guidance for when the header is required vs optional.
- [ ] Tests covering cases that today’s text-only fingerprint would merge incorrectly.

**Notes:** Optional follow-ups: reject fingerprint-only when multi-tenant auth is enabled; prefer client-supplied ids as the primary path in docs. Tenant-scoped fingerprints belong with [TODO-005](#todo-005--multi-key--per-tenant-api-key-management).

---

## TODO-002 — Persist user turns before upstream

| Field | Value |
| --- | --- |
| **Status** | `deferred` |
| **Priority** | Medium |
| **Area** | `ProxyChatCompletionService` prepare / complete |

**Summary:** On the normal path, new non-assistant messages are staged in `PrepareAsync` and saved in `CompleteAsync` after the upstream call. If upstream fails, that turn is not written to SQLite. Client history still has the message; the local DB does not.

**Workaround:** Rely on the client resending full history on retry. Treat the Comprexy DB as a record of completed turns, not failed attempts.

**Acceptance criteria:**

- [ ] User (and other new non-assistant) messages are durable before the upstream call, **or** documented product rule that only completed turns are stored.
- [ ] Retries / duplicate prepares do not double-insert sequences or corrupt `SyncedMessageCount`.
- [ ] Tests for upstream failure after prepare covering persistence behavior.

**Notes:** Prefer a clear idempotency story over a blind pre-upstream save that can leave orphan rows or sync drift.

---

## TODO-003 — Bound compression queue and observe drops

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ChannelCompressionQueue`, `CompressionBackgroundService` |

**Summary:** Per-conversation coalesce is already in place. Channels remain unbounded, and failed/`TryWrite` paths do not log. Under many distinct conversations waiting on a slow worker, memory can grow without a clear signal.

**Workaround:** Single-process / modest concurrency is fine today. Coalesce already prevents redundant jobs for the same conversation.

**Acceptance criteria:**

- [ ] Bounded high/normal channels (or a documented capacity) with drop + warn preferred over blocking the chat request path.
- [ ] Log (and optionally metric) when an enqueue is coalesced-skip vs capacity-drop vs `TryWrite` failure.
- [ ] Tests or a short ops note for expected behavior when the queue is full.

**Notes:** Do not block `Enqueue` on the request thread. Capacity should reflect distinct conversations waiting, not turns per second.

---

## TODO-004 — Reassembled chat response DTO preserves `tool_calls`

| Field | Value |
| --- | --- |
| **Status** | `deferred` |
| **Priority** | Low |
| **Area** | `ChatCompletionMapper.ToResponseDto`, `ChatMessageDto` |

**Summary:** The reassembled response DTO maps assistant `content` only. `tool_calls` and related fields are omitted. Non-streaming responses prefer `RawResponseJson` when present, so clients usually receive the full upstream body.

**Workaround:** Keep returning `RawResponseJson` for non-streaming completions. Do not remove raw passthrough until the DTO path is complete.

**Acceptance criteria:**

- [ ] Fallback reassembled responses include `tool_calls` (and other wire-relevant assistant fields) when `RawResponseJson` is absent.
- [ ] Tests covering tool-call responses on the reassembled DTO path.

**Notes:** Latent until raw passthrough is removed or `RawResponseJson` is missing.

---

## TODO-005 — Multi-key / per-tenant API key management

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | `apps/control-api` (issuance/admin), proxy auth enforce, conversation identity |

**Summary:** `Auth:RequiredApiKey` is a single shared secret. That fits local single-tenant use. On a shared server, every client with that key is the same principal — there is no per-user or per-tenant credential.

**Workaround:** Single-tenant deploy, or require a unique `X-Comprexy-Conversation-Id` per logical session and treat the shared API key as a gate only.

**Home (repo restructure):** Key **administration** (create/revoke/list, tenant mapping) belongs on the control plane (`apps/control-api`, `/v1/comprexy/api-keys` and related). The proxy **enforces** resolved key/tenant/quota/policy state on the data plane and must not own issuance or billing. See [`internal/repo-restructure.md`](../internal/repo-restructure.md) (Phase 7). Scaffolding `apps/control-api` and moving metrics query routes does **not** close this item.

**Acceptance criteria:**

- [ ] Support multiple client API keys (config and/or store), each mappable to a stable tenant/principal id (not the raw secret in conversation keys or logs).
- [ ] Control-api (or equivalent documented API) can manage keys for operators/dashboard; proxy does not expose key-admin as chat-adjacent endpoints.
- [ ] Auth middleware on the proxy accepts any valid key; rejects unknown keys when auth is enabled.
- [ ] Proxy obtains enforceable key/tenant/policy state without calling billing providers (shared store and/or cached policy — **decide after repo restructure**, with this item; not in the control-api metrics-move PR).
- [ ] Optional: scope fingerprint / conversation lookup by tenant id so identical opening text does not cross tenants (coordinate with [TODO-001](#todo-001--stronger-conversation-identity)).
- [ ] Docs for single-key local vs multi-key shared deploy; default remains single-key/single-tenant until multi-tenant is explicitly enabled.
- [ ] Persist tenant id on tenant-scoped records even in local single-tenant mode (deterministic default tenant; avoid nullable tenant ids).

**Notes:** Do not hash a single shared `RequiredApiKey` into fingerprints — it adds no separation. Prefer key-id / tenant-id after multi-key auth exists. Keep this `open` until the acceptance criteria above are met; control-api existence alone is insufficient.

---

## TODO-006 — Bound message and working memory loads per conversation

|| Field | Value |
|| --- | --- |
|| **Status** | `open` |
|| **Priority** | High |
|| **Area** | `EfConversationMessageRepository`, `EfWorkingMemoryRepository` |

**Summary:** Both `GetByConversationIdAsync` methods load ALL rows for a conversation with no limit:

```csharp
dbContext.ConversationMessages
    .Where(m => m.ConversationId == conversationId)
    .OrderBy(m => m.Sequence)
    .ToListAsync(cancellationToken);
```

For a conversation with thousands of messages, the entire history is loaded into memory on every single chat request — even though `ContextBuilder` and `RecentContextSelector` only use a recent window of messages (~50k tokens). The database does a full scan + sort of all messages, and the entire result set sits in memory.

**Workaround:** Current usage is acceptable for short conversations (<500 messages). Performance degrades linearly as conversations grow.

**Acceptance criteria:**

- [ ] `GetByConversationIdAsync` applies a `.Take()` limit based on the max context window token count, or loads only unfolded messages (`GetUnfoldedAsync`) plus a bounded set of recent folded messages.
- [ ] `EfWorkingMemoryRepository.GetByConversationIdAsync` is similarly bounded (fewer working memories, but still unbounded).
- [ ] Pagination fallback (`Skip()` / `Take()`) for conversations exceeding the limit.
- [ ] Integration test verifying query plan does not return full table for large conversations.

**Notes:** Coordinate with [TODO-007](#todo-007--add-caching-layer) — a caching layer can reduce the frequency of these loads but does not address the per-request unbounded scan.

---

## TODO-007 — Add caching layer for conversation data

| Field | Value |
| --- | --- |
| **Status** | `partial` |
| **Priority** | High |
| **Area** | `IMemoryCache`, repository layer, `ProxyChatCompletionService` |

**Summary:** Every request reloads the full conversation history from SQLite, rebuilds the context, re-estimates tokens, and re-evaluates the compression budget. There is no caching of:

- Conversation messages
- Working memories
- Context budget decisions

For a high-traffic scenario, the same data is loaded and processed repeatedly between user messages.

**Workaround:** Current usage is acceptable for low-to-moderate traffic. Each request pays the full DB read + context build cost.

**Completed:**

- [x] Token estimate caching (`TokenEstimateCache`) — in-memory `IMemoryCache` keyed by SHA-256 hash of input text/message, TTL-based expiration (15 min default), size-limited (10k entries), per-key lock for stampede protection. Registered as `ITokenEstimateCache` singleton. Configurable via `Comprexy:TokenEstimateCache` section.

**Acceptance criteria:**

- [ ] In-memory cache (e.g., `IMemoryCache`) keyed by `conversationId` with a short TTL (e.g., 5–30 seconds).
- [ ] Cache invalidation on write operations: message add, working memory creation, compression.
- [ ] Per-conversation lock or similar mechanism to avoid cache stampedes under concurrent requests.
- [ ] Cache hit/miss metrics or logging for operational visibility.

**Notes:** A caching layer reduces DB load frequency but does not address the per-request unbounded scan within [TODO-006](#todo-006--bound-message-and-working-memory-loads-per-conversation). Consider combining both optimizations.

---

## TODO-008 — Richer compact tool metadata and confirmation enforcement

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | `ToolSchema` compact index, validation gates |

**Summary:** MVP CompactIndex derive (A) copies `name`, `description`, and `parameters.required` only. The design wants `use_when`, `do_not_use_when`, `side_effect`, and `needs_confirmation` for better tool selection and safety. Confirmation for external / financial / destructive tools is prompt-only in MVP.

**Workaround:** Rely on client-authored `description` quality and hard hydrate + JSON Schema arg gates.

**Acceptance criteria:**

- [ ] Compact entries can carry richer fields from optional client extensions and/or derived heuristics.
- [ ] Optional enforcement path for `needs_confirmation` / high side-effect tools (beyond prompt text).
- [ ] Docs for extension shape and defaults when fields are absent.
- [ ] Tests for selection metadata present in compact index and confirmation behavior when enabled.

**Notes:** Plan: [`internal/plans/tools-schema-index.md`](../internal/plans/tools-schema-index.md). Coordinate with [TODO-009](#todo-009--llm-compact-tool-index-summaries) if summaries supply these fields.

---

## TODO-009 — LLM compact tool index summaries

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ToolSchema` snapshot, Compression endpoint |

**Summary:** MVP does not call an LLM to rewrite tool descriptions. For vague or near-duplicate client `description`s, an optional one-shot summarize (at catalog snapshot time, cached by `definitionHash`) could produce stronger compact entries (`use_when` / `do_not_use_when` / side-effect hints).

**Workaround:** Field-map derive (A); improve client tool descriptions upstream.

**Acceptance criteria:**

- [ ] Opt-in setting; default off.
- [ ] Runs at snapshot time (or offline), not on every chat turn; reuse Compression (or dedicated) model endpoint.
- [ ] Output validated to compact entry shape; fall back to derive (A) on failure.
- [ ] Cache by tool `definitionHash` so unchanged tools are not re-summarized.
- [ ] Tests for success, fallback, and cache hit.

**Notes:** Do not block CompactIndex MVP. Prefer after [TODO-008](#todo-008--richer-compact-tool-metadata-and-confirmation-enforcement) field shape exists. Plan: [`internal/plans/tools-schema-index.md`](../internal/plans/tools-schema-index.md).

---

## TODO-010 — Grouped tool catalog and `list_tools_in_group`

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ToolSchema` meta-tools, compact index |

**Summary:** For very large catalogs, the design adds a group index plus `list_tools_in_group` before per-tool `get_tool_definition`. MVP is two-stage only (compact index → full def).

**Workaround:** CompactIndex with `MinToolCountToActivate`; accept a larger flat index until catalogs demand grouping.

**Acceptance criteria:**

- [ ] Optional group index in the snapshotted system payload when enabled / above a threshold.
- [ ] Proxy-local `list_tools_in_group` meta-tool returning compact summaries for that group.
- [ ] Hard gates treat the new meta-tool like `get_tool_definition` (allowed without prior hydration).
- [ ] Docs and tests for group → compact → full-def flow.

**Notes:** Plan: [`internal/plans/tools-schema-index.md`](../internal/plans/tools-schema-index.md). Design: [`internal/tool-calls-schema-index.md`](../internal/tool-calls-schema-index.md).

---

## TODO-011 — Tool catalog mismatch Refresh setting

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | `ToolSchema` snapshot, `ConversationToolCatalog` |

**Summary:** MVP assumes client `tools[]` is stable for a conversation. On inbound catalog hash mismatch vs snapshot, Comprexy keeps the snapshot and warns. Operators may need `OnCatalogMismatch = KeepSnapshot | Refresh`.

**Workaround:** Start a new conversation (new `X-Comprexy-Conversation-Id`) when the tool catalog intentionally changes.

**Acceptance criteria:**

- [ ] `ToolSchema:OnCatalogMismatch` setting with default `KeepSnapshot`.
- [ ] `Refresh` rebuilds catalog snapshot, compact index, and clears or rehashes per-tool hydration as needed.
- [ ] Logging for mismatch and chosen policy.
- [ ] Docs in SETTINGS.md; tests for Keep vs Refresh.

**Notes:** Plan: [`internal/plans/tools-schema-index.md`](../internal/plans/tools-schema-index.md).

---

## TODO-012 — Stricter tool JSON Schema dialect subset

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ToolSchema` arg validation |

**Summary:** MVP validates tool call arguments with a real JSON Schema library against each tool’s `parameters` as sent. Exotic or inconsistent dialects may yield noisy fail-closed errors.

**Workaround:** Fail closed with the validator message in the synthetic JSON tool error; fix upstream tool schemas.

**Acceptance criteria:**

- [ ] Optional mode that validates against a documented OpenAI-oriented subset (e.g. type, properties, required, enum, items).
- [ ] Clear docs for supported keywords vs ignored/rejected.
- [ ] Tests for subset accept/reject behavior vs full-validator mode.

**Notes:** Only pursue if production catalogs prove noisy. Plan: [`internal/plans/tools-schema-index.md`](../internal/plans/tools-schema-index.md).
