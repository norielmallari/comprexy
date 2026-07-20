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
| **Area** | `AuthOptions`, `ApiKeyAuthMiddleware`, conversation identity |

**Summary:** `Auth:RequiredApiKey` is a single shared secret. That fits local single-tenant use. On a shared server, every client with that key is the same principal — there is no per-user or per-tenant credential.

**Workaround:** Single-tenant deploy, or require a unique `X-Comprexy-Conversation-Id` per logical session and treat the shared API key as a gate only.

**Acceptance criteria:**

- [ ] Support multiple client API keys (config and/or store), each mappable to a stable tenant/principal id (not the raw secret in conversation keys or logs).
- [ ] Auth middleware accepts any configured key; rejects unknown keys when auth is enabled.
- [ ] Optional: scope fingerprint / conversation lookup by tenant id so identical opening text does not cross tenants (coordinate with [TODO-001](#todo-001--stronger-conversation-identity)).
- [ ] Docs for single-key local vs multi-key shared deploy.

**Notes:** Do not hash a single shared `RequiredApiKey` into fingerprints — it adds no separation. Prefer key-id / tenant-id after multi-key auth exists.

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
