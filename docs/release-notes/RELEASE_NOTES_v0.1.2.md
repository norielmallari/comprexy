# Comprexy OSS v0.1.2

Follow-up to [`v0.1.1`](RELEASE_NOTES_v0.1.1.md): Virtual Tools observation honesty and cache completeness, mid-chain Inline wrap-up under soft pressure, provider-aware metrics reads, a reproducible benchmark harness, and Apache 2.0 licensing.

This repository is the Apache 2.0–licensed open core of Comprexy OSS. Further product work may also continue separately as Comprexy.

## Highlights

- **Honest Virtual Tools observations** — Distilled IR results disclose requested vs returned spans, `body_complete` / `complete` / `next_start_line`, search truncation split flags, search sentinels as zero matches, and dir `total_entry_count`. Caps move into `ToolSchema` options and are disclosed on Virtual tool descriptions. Optional `end_line` enables an unwindowed first read (capped by `FirstReadMaxLines` / `FirstReadMaxChars`).
- **Cache completeness** — File-body cache entries carry `BodyComplete` / `TotalLineCount`. Incomplete bodies never local-satisfy (full rematerialize). Envelope unwrap is gated so file text that merely contains `<content>` is not truncated. Local-satisfy IR turns are persisted so the model keeps its cache-hit history.
- **Optional idle shape learner** — First-result shape probes plus an opt-in idle learner (`ToolSchema:ResultShape:Learner`, default off) can promote closed `result_shapes` into MappingJson. The learner waits on `IUpstreamActivityGate`, is preempted by chat/upstream traffic, and never blocks a turn or takes a UoW.
- **Mid-chain Inline wrap-up** — Soft pressure can checkpoint a repaired closed prefix while a tool chain is still open; streaming holds the client `tool_calls` tail until wrap-up resolves.
- **Benchmark harness** — `tests/Comprexy.Bench` runs scripted MAF conversations against proxy/control-api fleets, joins control-api metrics, and reports survival / evidence summaries.
- **Apache 2.0** — License and project-direction docs align on Apache-2.0 open core (with trademark terms for separate Comprexy branding).

## Added

- Observation honesty fields and configurable distill caps (`FirstRead*`, `MaxSearchPreviewChars`, `SearchSentinelMaxChars`, manifest/dir caps, and related SETTINGS rows)
- File-cache completeness gate; optional unwindowed `comprexy_read_file_range`; cap disclosure on Virtual tool descriptions
- Result-shape probe/store, MappingJson `result_shapes` mirror (tracked catalog write), and optional idle shape learner + upstream activity gate
- Persistence of pure local-satisfy IR turns (`PendingPersistedTurns`)
- Mid-chain Inline wrap-up path and streaming tail hold for eligible open-chain turns
- `Metrics:PromptTokenBasis` (`ProviderActual` default) and optional `?promptTokenBasis=` on metrics REST; turn wall-clock duration columns on `ConversationTurnMetric`
- `Comprexy.Bench` CLI + frozen conversation scripts under `tests/Comprexy.Bench.Conversations`
- IDE agent/rules mirroring and `task` kept visible for delegation
- Reviewer evidence gates (E1–E7) in plan/backend/UI reviewer prompts

## Changed

- Virtual Tools mapping salvage: drop only failed bindings when at least one usable binding survives (avoids disabling Tool IR for the whole catalog hash on a single bad binding)
- Local-satisfy and inbound distill commit also flush staged `result_shapes` when present
- Metrics reads can project provider-actual prompt totals without rewriting SoftBudget persistence
- README / `docs/ARCHITECTURE.md` / `docs/SETTINGS.md` updated for the above

## Fixed

- Never drop the newest turn from the outgoing prompt (Cache Alignment / tip guard)
- Salvage Virtual Tools mapping when one binding fails validation
- Align live extract prefix attestation with replay (`LinePrefix` strict equality, including `None`)

## Upgrade notes

- **From v0.1.1:** apply EF migrations (or recreate the DB). This release adds nullable turn-duration columns (`PrepareDurationMs` / `UpstreamDurationMs` / `DurationMs`) via `20260801131606_AddTurnMetricDurations`. MappingJson may gain optional `result_shapes`; no separate table is required.
- **From v0.1.0 / `v0.1.0-preview`:** still delete/recreate SQLite before first start (`./comprexy.sh clear-db` / `.\comprexy.cmd clear-db`) — preview → 0.1.1 schema was not upgrade-compatible.
- Idle shape learning stays **off** unless you set `ToolSchema:ResultShape:Learner:Enabled` to `true`. Incomplete file-cache entries no longer local-satisfy; expect more native rematerialize reads until complete bodies are cached (often via unwindowed first reads).
- Review new `ToolSchema` / `Metrics:PromptTokenBasis` knobs in [`docs/SETTINGS.md`](../SETTINGS.md).
- Benchmark harness: see README § Benchmark harness (`tests/Comprexy.Bench`).
