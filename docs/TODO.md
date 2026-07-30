# Backlog

Deferred and planned work for Comprexy. Prefer [GitHub Issues](https://github.com/norielmallari/comprexy/issues) when an item needs discussion, assignees, or cross-PR tracking.

| Status | Meaning |
| --- | --- |
| `open` | Ready to implement |
| `deferred` | Accepted for now; documented workaround or lower priority |
| `partial` | Some acceptance criteria done; remainder still open |

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

## TODO-006 — Bound message loads per conversation

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | High |
| **Area** | `EfConversationMessageRepository`, chat prepare |

**Summary:** `GetByConversationIdAsync` loads all message rows for a conversation with no limit. Chat prepare still calls it, so long conversations pay a full scan + materialization on every request even though rebuild uses working memory plus unfolded / tip messages.

**Workaround:** Acceptable for short conversations. Cost grows with total stored message count (including folded).

**Acceptance criteria:**

- [ ] Hot-path loads use bounded queries (e.g. unfolded + recent tip, or token/window-bounded `Take`) instead of unbounded `GetByConversationIdAsync`.
- [ ] Callers that still need a full history (if any) use an explicit, documented API — not the chat hot path.
- [ ] Tests covering large conversations that the hot path does not materialize every row.

**Notes:** Coordinate with [TODO-007](#todo-007--add-caching-layer). Working-memory hot path already uses `GetLatestAsync` / versioned reads; no separate unbounded WM list on chat prepare.

---

## TODO-007 — Add caching layer for conversation data

| Field | Value |
| --- | --- |
| **Status** | `partial` |
| **Priority** | High |
| **Area** | `IMemoryCache`, repository layer, `ProxyChatCompletionService` |

**Summary:** Every request reloads conversation messages from SQLite, rebuilds the context, re-estimates tokens, and re-evaluates the soft budget. There is no caching of conversation messages, working memories, or budget decisions between requests.

**Workaround:** Acceptable for low-to-moderate traffic. Each request pays the full DB read + context build cost.

**Completed:**

- [x] Token estimate caching (`TokenEstimateCache`) — in-memory `IMemoryCache` keyed by SHA-256 hash of input text/message, TTL-based expiration (15 min default), size-limited (10k entries), per-key lock for stampede protection. Registered as `ITokenEstimateCache` singleton. Configurable via `Comprexy:TokenEstimateCache` section.

**Acceptance criteria:**

- [ ] In-memory cache (e.g., `IMemoryCache`) keyed by `conversationId` with a short TTL (e.g., 5–30 seconds).
- [ ] Cache invalidation on write operations: message add, working memory creation, Inline wrap-up.
- [ ] Per-conversation lock or similar mechanism to avoid cache stampedes under concurrent requests.
- [ ] Cache hit/miss metrics or logging for operational visibility.

**Notes:** Caching reduces DB load frequency but does not replace [TODO-006](#todo-006--bound-message-loads-per-conversation) bounded loads. Cache Alignment (`ICacheAlignmentService`, `CacheAlignment` options) is adjacent provider message-prefix / KV work — it does **not** close the TTL conversation-row cache ACs above.

---

## TODO-008 — Richer Virtual file-tool observations

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | `ToolSchema` Virtual Tools, distillation |

**Summary:** MVP file observations are compact but shallow (heuristic imports/symbols, capped search/dir hits). Richer manifests (AST symbols, structured imports/exports) and safer confirmation for high-risk native backends would improve selection without exposing raw IDE file tools.

**Workaround:** Rely on tool descriptions and range/search caps (`MaxRangeLines` / `MaxSearchMatches`).

**Acceptance criteria:**

- [ ] Optional richer manifest fields from native metadata tools when the mapping exposes them.
- [ ] Optional confirmation / high-risk gate for destructive native backends (beyond prompt text).
- [ ] Docs for observation shape and defaults when fields are absent.
- [ ] Tests for richer observation fields and confirmation behavior when enabled.

**Notes:** Plan: [`internal/plans/virtual-tools.md`](../internal/plans/virtual-tools.md).

---

## TODO-009 — Process-wide MappingJson cache by schema hash

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ToolSchema` mapping, Compression endpoint |

**Summary:** MVP persists MappingJson per conversation. Across conversations in one process that share the same catalog hash, the mapper may re-run. A process-wide memory cache keyed by `schema_hash` would skip re-LLM.

**Workaround:** Accept per-conversation mapping cost; DisableToolIr still avoids retries after failure for that conversation hash.

**Acceptance criteria:**

- [ ] Optional in-process cache keyed by `schema_hash` with TTL/size bounds.
- [ ] Never cache invalid maps; DisableToolIr remains conversation-scoped unless product decides otherwise.
- [ ] Escalate before adding a shared durable hash table.
- [ ] Tests for hit/miss and invalid-map exclusion.

**Notes:** Plan: [`internal/plans/virtual-tools.md`](../internal/plans/virtual-tools.md).

---

## TODO-010 — Virtualize additional non-file tool families

| Field | Value |
| --- | --- |
| **Status** | `partial` |
| **Priority** | Low |
| **Area** | `ToolSchema` Virtual Tools |

**Summary:** File Virtual Tools plus Shell family (`SHELL_BACKEND` / `comprexy_shell`) ship via `VirtualToolRegistry`. Edit/write/ApplyPatch, MCP, and browser remain full-schema passthrough.

**Workaround:** Remaining non-virtualized tools pass through unchanged when Virtual is active.

**Acceptance criteria:**

- [x] Mapping schema extended for the Shell Virtual tool family (`SHELL_BACKEND`, `comprexy_shell`).
- [x] Planner + distill paths for Shell without an inner invisible multi-tool loop.
- [x] Docs and tests for outbound surface and wire remap (Shell).
- [ ] Edit / write / ApplyPatch Virtual family.
- [ ] MCP / browser Virtual families.

**Notes:** Plan: [`internal/plans/shell-ir.md`](../internal/plans/shell-ir.md). Archived file-IR plan: [`internal/archive/plans/virtual-tools.md`](../archive/plans/virtual-tools.md).

---

## TODO-011 — Tool catalog mismatch policy knobs

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | `ToolSchema` snapshot, `ConversationToolCatalog` |

**Summary:** Virtual MVP remaps on inbound catalog hash mismatch (blocking). Operators may want explicit `OnCatalogMismatch` policies (always remap vs require new conversation) and timeouts for the mapper.

**Workaround:** Start a new conversation when the catalog intentionally changes in a way that should not block chat.

**Acceptance criteria:**

- [ ] Documented mismatch policy setting(s) with clear defaults.
- [ ] Optional mapper timeout separate from Compression timeout.
- [ ] Logging for mismatch and chosen policy.
- [ ] Docs in SETTINGS.md; tests for policy behavior.

**Notes:** Plan: [`internal/plans/virtual-tools.md`](../internal/plans/virtual-tools.md).

---

## TODO-012 — Stricter tool JSON Schema dialect subset

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Low |
| **Area** | `ToolSchema` arg validation |

**Summary:** MVP validates tool call arguments with a real JSON Schema library against each tool’s `parameters` as sent (Virtual schemas and passthrough client defs). Exotic or inconsistent dialects may yield noisy fail-closed errors.

**Workaround:** Fail closed with the validator message in the synthetic JSON tool error; fix upstream tool schemas.

**Acceptance criteria:**

- [ ] Optional mode that validates against a documented OpenAI-oriented subset (e.g. type, properties, required, enum, items).
- [ ] Clear docs for supported keywords vs ignored/rejected.
- [ ] Tests for subset accept/reject behavior vs full-validator mode.

**Notes:** Only pursue if production catalogs prove noisy.

---

## TODO-013 — Metrics rollup for subagent conversations

| Field | Value |
| --- | --- |
| **Status** | `open` |
| **Priority** | Medium |
| **Area** | Conversation metadata, metrics query (control-api REST / MCP), dashboard |

**Summary:** Parent-session metrics exclude spawned subagent conversations (Task / cloud runners and similar). Those sessions are separate `Conversation` rows with their own turn metrics, so workflow-level baselines and savings undercount. Linking must not merge transcripts; rollup is a query concern over linked conversations.

**Workaround:** Collect child conversation ids manually (response header / `comprexy_get_current_conversation_id`) and use `comprexy_compare_conversations` or external summation. See evidence coverage note in [`docs/evidence/5ca87ca.md`](evidence/5ca87ca.md).

**Acceptance criteria:**

- [ ] Optional parent link persisted on child `Conversation` from an explicit client signal (header and/or operator attach).
- [ ] Query API returns parent + children + summed token/savings rollup without merging transcripts.
- [ ] REST and MCP expose the tree/rollup; single-conversation tools keep current semantics.
- [ ] Dashboard can show workflow rollup for a parent with linked children (or document deferred UI if API-first).
- [ ] Architecture / SETTINGS / evidence guidance updated; tests for link ingest + rollup math.
- [ ] Documented limitation: unproxied / pass-through children remain invisible to Comprexy metrics.

**Notes:** Research and plan: [`docs/plans/metrics-subagents.md`](plans/metrics-subagents.md). Prefer `X-Comprexy-Parent-Conversation-Id` + `ParentConversationId` FK; keep compression per conversation. Depends on clients (or a wrapper) setting the parent header for automatic linking.
