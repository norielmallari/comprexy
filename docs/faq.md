# FAQ

## What Comprexy OSS is not

- Not a model or LLM runtime — it proxies to your configured upstream.
- Not a multi-provider gateway, router, or billing layer.
- Not a vector database or retrieval framework.
- Not a static prompt minifier or offline context packer.
- Not a guarantee of better answers or higher tok/s; it manages prompt size and structure so long sessions stay usable.
- Not a guarantee of agent quality, model correctness, code correctness, workflow success, or actual cloud bill reduction.

## How does conversation identity work?

Comprexy OSS prefers a unique `X-Comprexy-Conversation-Id` header per session. When that header is omitted, identity is derived from the system prompt and the first two **plain** user turns (Cursor `<user_query>` extraction when present; tool-echo user turns are skipped). Sessions that share the same opening text can map to one stored conversation.

For multi-tab or multi-user setups, send an explicit `X-Comprexy-Conversation-Id` header.

## What roles are supported?

Chat compression supports `system`, `user`, `assistant`, and `tool` roles. Other roles (for example `developer`) are rejected on `/v1/chat/completions`.

## What is Pass-through mode?

`Proxy:PassThrough` forwards the original client body unmodified — no rebuild, compression, Virtual Tools rewrite, or client rules path. It is an escape hatch; leave it off for normal use. When `Proxy:PassThrough` is enabled, the client's `model` field is forwarded as sent unless `Provider:Model` overrides it.

## How does client rules management work?

On each non-PassThrough prepare, Comprexy OSS extracts rule bodies from the latest client system message and from new user/tool transcript slices (Cursor and Kilo wire markers). Rules are consolidated by key with replace semantics, injected as ephemeral system overlays when not yet present in working memory, and written into WM `## Rules` on a successful Inline accept. Synthetic rule messages are not persisted as conversation rows. Comprexy does not re-evaluate `.mdc` globs or `alwaysApply` — it trusts which rules the client already attached. See [Architecture](ARCHITECTURE.md) (Outgoing context / Client rules).

## Does working memory persist across restarts?

Yes. Persisted message records, working-memory versions, and metrics are stored in SQLite and survive process restarts. The system prompt captured on the first turn is reused when rebuilding context after a restart.

## How accurate are the token estimates?

Token estimates use tiktoken for text and OpenAI-style vision tiles for `image_url` (base64 is not BPE-counted). SoftBudget persistence and wrap-up eligibility stay estimate-based on prepared size. SoftBudget headline savings use IrFull (IR tools + full unfolded IR transcript) vs Prepared when IrFull is present; NativeRaw − IrFull is a separate virtual-tools / native-wire channel and can be negative. Metrics API reads default to `PromptTokenBasis=ProviderActual` when upstream `usage.prompt_tokens` is present. Actual provider billing may differ because of model-specific tokenization, prompt caching, output volume, provider pricing, local hardware utilization, and workflow shape.

## Can I use Comprexy OSS with non-OpenAI upstreams?

Yes. Any OpenAI-compatible endpoint works — Ollama, LM Studio, vLLM, Azure OpenAI-compatible APIs, and similar. Configure the `Provider:BaseUrl` in your settings.

## Where is the source of truth for a conversation?

Comprexy OSS persists completed conversation turns as the durable record. Working memory is a derived, versioned representation used to construct bounded upstream prompts. Compression marks messages as folded; it does not delete or replace them.

## What happens on soft budget pressure?

Soft (`> soft`) above `SoftLimitTokens` triggers a blocking Inline wrap-up on eligible turns (closed stored tool chain, or mid-chain checkpoint of a repairable closed prefix). The wrap-up folds older unfolded messages into a new working-memory version while retaining a tip window (`CompressionRetainMessageCount`). Soft failure never overwrites the last known-good working memory.

## Are there known limitations?

- Soft Inline wrap-up and the conversation gate are process-local; they are not shared across multiple API instances.
- Virtual Tools mapping is best-effort per catalog hash. A catalog with no tool the mapper can bind loses that Virtual tool only.
- Client rules extraction is best-effort against known Cursor/Kilo wire markers; unrecognized formats are left in BaseSystem or transcript text.
- Incomplete file-body cache entries never local-satisfy; the proxy rematerializes until a complete body is cached.
- `ExcludeFromModelTools` hides tools from the model only; they remain in the client catalog. Already-persisted transcript turns are not scrubbed.

See [Configuration](SETTINGS.md) for all settings and defaults.
