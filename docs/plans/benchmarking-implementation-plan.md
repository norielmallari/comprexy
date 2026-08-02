# Benchmarking harness — implementation plan

Plan for an MAF-driven coding-agent benchmark that compares **client-side MAF compaction at 256k** against **Comprexy compression + Virtual Tools**, with harness-owned proxy/control-api processes, disk manifests, and a separate report step suitable for `docs/evidence/` and website copy.

Supersedes the earlier sketch in `docs/plans/test-harness-maf-implementation-plan.md` where they conflict.

## Goal

Simulate a human coding with an agent (read/write files and shell under a sandbox) by feeding a frozen prompt list through Microsoft Agent Framework (MAF). Run the **same prompts** twice:

| Arm | Role | Comprexy | MAF client compaction |
| --- | --- | --- | --- |
| `maf-compact` | Baseline — “client alone” | `ToolSchema:Mode=Off`, soft limit overridden unreachable (`100_000_000`) | MAF `MaxContextWindowTokens` default **256_000** (this *is* the compression) |
| `comprexy` | Treatment | `ToolSchema:Mode=Virtual`, soft limit from the normal appsettings chain (no harness override) | Same MAF default **256k** as backstop; should rarely fire |

Arms run **sequentially** (not in parallel). Metrics come from Comprexy’s existing turn/summary tables via control-api / telemetry MCP. The harness does not recompute token savings.

Do **not** use `Proxy:PassThrough` for the baseline — pass-through skips turn metrics and leaves nothing to compare.

## Non-goals

- Per-request headers to disable compression (mode mixing corrupts stored IR/native history)
- Bench result tables in the Comprexy schema
- Parallel arms or shared live working-tree writes
- Auto-committing website copy from a hot run
- Merge-default Playwright against live control-api (evidence capture is opt-in/live; smokes stay mocked)

## Architecture

```text
┌─ Comprexy.Bench ─────────────────────────────────────────────┐
│  ProxyArmHost / ControlApiHost (spawned processes)           │
│  MaF agent (file + shell tools → bench-workspace/)           │
│  MaF MaxContextWindowTokens default 256k (both arms)         │
│  manifest.json + harness wall clocks                         │
└───────────────┬───────────────────────────────┬──────────────┘
                │ chat :18129 / :18131          │
                ▼                               ▼
         proxy maf-compact              proxy comprexy
                │                               │
                └───────────┬───────────────────┘
                            ▼
                 data/comprexy-bench.db  ◄── control-api (:18130)
                            ▲
                            │ REST + /mcp
              bench report (MAF + telemetry MCP)
              optional Playwright → dashboard (:3000)
```

| Piece | Responsibility |
| --- | --- |
| `tests/Comprexy.Bench` | CLI (`run` / `report` / `publish`), spawn hosts, drive MAF, write `reports/bench/<runId>/` |
| Proxy arms | Real Kestrel hosts; env overrides for mode (both arms), soft limit (`maf-compact` only), DB, trace dir |
| Control-api | Same bench DB; serves metrics REST + MCP for report/screenshots |
| Dashboard | Optional live UI for evidence screenshots only |
| `docs/evidence/` | Curated, committed summaries (+ optional PNGs) after human review |

## Product prerequisite — config precedence

Today `UseRepoSharedDatabase` and `appsettings.Local.json` are registered **after** the default env/command-line providers, so harness overrides lose to Local.json (including `ToolSchema:Mode` and `ConnectionStrings:Comprexy`).

**Fix in both** `apps/proxy/Program.cs` and `apps/control-api/Program.cs`: after Local.json, re-append:

```csharp
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
```

Document the new precedence in `docs/SETTINGS.md` (env/cmdline after SharedSqlite + Local.json).

## Host spawning

Harness builds host assemblies once, then starts the **DLL** with `dotnet` (not `dotnet run`) so `Process` owns Kestrel and `Kill(entireProcessTree: true)` tears down cleanly.

- `WorkingDirectory` = `apps/proxy` or `apps/control-api` (so appsettings resolve like a normal run)
- Bind dedicated ports (default): proxies `18129` (`maf-compact`), `18131` (`comprexy`); control-api `18130`
- Ready = poll `/health` with timeout; on failure dump arm log tail
- Start control-api + first proxy, wait healthy, then second proxy (avoid dual `Migrate()` races)
- Redirect stdout/stderr to `reports/bench/<runId>/logs/<arm>.log`
- Wire `CancelKeyPress` + `finally` for teardown
- CLI `--no-spawn` keeps pointing at already-running hosts for debugging

### Passing config

Standardize on **`ProcessStartInfo.Environment`** (`__` → `:`) plus `--urls` on the argument list.

Shared (every bench host):

| Env | Example |
| --- | --- |
| `ConnectionStrings__Comprexy` | `Data Source=<repo>/data/comprexy-bench.db;Cache=Shared` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |

Per proxy arm:

| Env | `maf-compact` | `comprexy` |
| --- | --- | --- |
| `ToolSchema__Mode` | `Off` | `Virtual` |
| `ContextPolicy__SoftLimitTokens` | `100000000` | *(omit — use appsettings chain)* |
| `Trace__RequestLogDirectory` | per-arm under run dir (optional) | same |

Unset keys are not blanked — Local.json / appsettings still supply `Provider:*` and, on the `comprexy` arm, `ContextPolicy:SoftLimitTokens`. Set `ToolSchema__Mode` explicitly on both arms so the comparison does not depend on whatever Local.json happens to pin. Override soft limit only on `maf-compact` so Comprexy compression cannot fire; the treatment arm keeps the operator’s normal soft-limit default from the host config chain. Record the **resolved** soft limit in `manifest.json` (read back from the running host or from the same config sources the process loads).

## Database

- Default file: `data/comprexy-bench.db` (gitignored like other `data/*.db`)
- **One DB for the whole run**: both proxies, control-api, MCP report tools, and dashboard evidence
- Reporting **must** use a control-api pointed at that same connection string; otherwise compare/turns/screenshots miss the run
- Overrideable to dogfood `comprexy.db` if desired; dedicated file avoids wipe/clutter risk

No new tables for bench runs or manifests.

## MAF agent (run step)

- Packages: `Microsoft.Agents.AI`, OpenAI client toward the arm’s proxy base URL, Harness file tools (`FileAccessProvider`), and shell (`LocalShellExecutor` / MAF shell package)
- **Client compaction**: armed on `maf-compact` only, at harness default `MaxContextWindowTokens = 256_000` (and matching output budget); CLI-overridable, recorded per arm in the manifest. It is the baseline's treatment, so arming it on `comprexy` too would put two compressors in one arm with no way to attribute a result to either
- **Validity**: MAF compaction on `maf-compact` is expected signal. `ClientCompactionCount` is null on an arm where the strategy was never armed, which is a different fact from an armed strategy that never fired; reporting must not collapse the two
- **Tools**: real MAF **file and shell** tools, not dummy schemas. Root file access (and shell cwd) at `reports/bench/<runId>/workspace/<arm>/<conversation>/`, which is a **throwaway `git clone` of this repository checked out at the run's HEAD commit** — never the live working tree. Real Comprexy source is the point: a toy fixture cannot produce the context volume the proxy exists to manage, and both arms get the same pinned commit. Uncommitted work is deliberately absent, so a run is reproducible from the manifest. Each conversation's diff **against the pinned commit** is captured to `<arm>/<conversation>.patch`, then the clone directory is deleted. Cap per-command shell wall time in harness options
- **Why a clone, not a worktree**: a worktree shares the developer's object store and ref namespace, so an agent that commits or branches writes into the real repository and a failed teardown leaves refs behind. The clone is cloned with `--no-hardlinks` and has its `origin` removed, so it owns its objects and has no path back; teardown is `rm -rf`, which cannot half-succeed into the real repository. Measured cost on this repo is ~80 ms and ~5.6 MB per workspace. Filesystem access outside the workspace is still not OS-enforced — the shell tool is scoped by working directory only
- **Prompts**: frozen JSON lists under `tests/Comprexy.Bench.Conversations/`; same files for both arms
- **Fixed context floor**: every conversation sends `agent-preamble.md` ahead of its scenario text, and the tool schemas carry client-density descriptions. A real coding client spends ~11k tokens on its system prompt and ~8k on tool schemas before the first user turn; a one-line instruction with terse schemas would measure a workload Comprexy never sees. The composed system prompt is folded into the prompt-list hash so drift breaks pairing
- **Conversation ids**: new GUIDs per arm conversation; send `X-Comprexy-Conversation-Id`
- **Timeouts** (harness options, not product config):
  - Per-completion / HTTP timeout (local 64k+ outputs can stall)
  - Per-conversation wall-clock cap
  - Terminal status `timed_out` → excluded from paired headlines
- **Local LLM**: cost/caching/order bias accepted; sequential arms avoid GPU contention
- Temperature 0 / seed when the provider honors them

## Timing metrics

### Product schema (in scope — migration on `ConversationTurnMetric`)

| Field | Status | Meaning |
| --- | --- | --- |
| `RequestStartedAt` | Exists | Turn accept |
| `CreatedAt` | Exists | Metric row write |
| `DurationMs` | **Add** | Full proxy turn wall clock (prepare → upstream → complete) |
| `UpstreamDurationMs` | **Add** | Time blocked on provider HTTP only |
| `PrepareDurationMs` | **Add** | Prepare/path work before upstream |

Keep `CompressionEvent.DurationMs` as wrap-up-only.

Ship these columns in Phase 0 via `dotnet ef migrations`, and expose them on control-api turn DTOs and telemetry MCP turn payloads in the same change. Do not rely on deriving duration from `CreatedAt - RequestStartedAt` for published metrics.

### Harness manifest only (not DB)

| Field | Meaning |
| --- | --- |
| `ConversationWallClockMs` | Full agent loop including tool execution |
| `ArmWallClockMs` | Sum for that arm |
| `CompletionTimeoutMs` / `ConversationTimeoutMs` | Configured caps |
| `TimedOut` / terminal status | Per conversation |

Website copy should label proxy-turn timing vs full-agent wall clock so claims stay precise.

## Results handling

### Run artifacts (`reports/bench/<runId>/` — gitignore)

`<runId>` is the UTC `yyyyMMdd-HHmm` stamp of the run's start, with an optional `--run-id` label appended (`20260801-1200-short-deep`). `run` refuses to start in a directory that already holds a `manifest.json`, so artifacts are never silently overwritten.

| Artifact | Role |
| --- | --- |
| `manifest.json` | Provenance (Comprexy commit, model, MAF version, resolved arm settings, conversation GUIDs, prompt-list hash, validity flags, harness clocks, timeouts) |
| `metrics.json` | Deterministic join from control-api (tokens, savings, turns, durations) |
| `summary.md` | Draft narrative for review |
| `logs/`, optional traces, workspace | Debug only |
| optional `*.png` | Live dashboard screenshots |

### Pairing rules

Headline comparisons include only conversations that:

- Completed in **both** arms
- Share the same prompt-list hash
- Are not `timed_out` / errored
- Note `comprexy`-arm MAF compaction if it fired (call out; do not hide)

Everything else listed as excluded with reason.

### CLI split

| Command | Does |
| --- | --- |
| `bench run` | Spawn hosts (unless `--no-spawn`), run arms sequentially, write manifest (+ raw logs) |
| `bench report` | Ensure control-api on bench DB; build `metrics.json`; MAF report agent → `summary.md`; optional Playwright shots |
| `bench publish` | Human-gated copy of curated markdown (+ PNGs) into `docs/evidence/` |

Do not embed analysis inside `run` — runs cost wall clock on local LLMs and should stay re-reportable.

## Report agent (MAF)

Separate small agent for `bench report`:

- Tools: telemetry MCP (`comprexy_compare_conversations`, growth timeline, budget events, evidence markdown, turn/summary tools) against the **bench** control-api
- Input: `manifest.json` (id pairs + validity)
- Output: `summary.md` with a fixed outline (method → results table → interpretation → caveats)
- **Numbers block**: emit deterministic figures from `metrics.json` in C#; LLM writes prose around them only
- **Tone**: inject `.cursor/rules/documentation-tone.mdc` (and “factual dogfood, not marketing” from agents-readme sync) into the system prompt — calm, precise claims, no audit theater, no overclaims, no secrets/log harvests/local paths
- Temperature 0; instruct to quote only tool-backed metrics
- Pass/fail and pairing stay deterministic in the harness

## Website / evidence in repo

Follow existing dogfood evidence under `docs/evidence/` (linked from README):

- Commit curated `docs/evidence/bench-<runId>.md` (+ optional PNGs) after review
- Keep `reports/bench/` gitignored
- Obey test-privacy / docs-tone: no real machine paths, no pasted request logs, no PII in committed evidence

### Optional Playwright screenshots

Possible via existing `apps/dashboard` Playwright stack, but **not** the mocked smoke suite.

- Live control-api on the bench DB; dashboard `NEXT_PUBLIC_API_BASE_URL` → that host
- Separate evidence project/tag (e.g. `e2e/evidence/`); `goto` conversation URL; wait on role/`data-testid`; full-page screenshot
- Invoked from `bench report` / `publish`, never as merge-default CI
- Failure to screenshot must not invalidate token metrics

## Implementation phases

### Phase 0 — Product prerequisites

- [x] Re-append env + command line after Local.json in proxy and control-api
- [x] Update `docs/SETTINGS.md` precedence notes
- [x] Add `DurationMs`, `UpstreamDurationMs`, `PrepareDurationMs` to `ConversationTurnMetric` + migration via `dotnet ef`; wire DTOs/MCP

### Phase 1 — Harness hosts + CLI skeleton

- [x] `ProxyArmHost` / `ControlApiHost` (build DLL, env, health, logs, teardown) — `Hosting/BenchHostProcess`, `Hosting/BenchHostFleet`
- [x] Dedicated bench DB path; shared connection string on all spawned hosts
- [x] CLI: `run` / `report` / `publish`, `--arm`, `--no-spawn`, timeout options
- [x] Gitignore `reports/bench/` (sandbox workspaces live under the run directory, so no second ignore rule)

### Phase 2 — MAF conversation runner

- [x] Agent → arm proxy URL; file **and shell** tools rooted at sandbox; MAF compaction default 256k (both arms)
- [x] Frozen conversations; GUID conversation ids; sequential arms
- [x] Completion + conversation timeouts; manifest writer (include resolved soft limit per arm)

### Phase 3 — Metrics join + report agent

- [x] Pull turns/summaries (including new duration fields) from bench control-api into `metrics.json`
- [x] Pairing + validity
- [x] MAF report agent + documentation-tone prompt → `summary.md`

### Phase 4 — Evidence polish

- [x] `publish` → `docs/evidence/`
- [x] Optional live Playwright evidence shots — `apps/dashboard/playwright.evidence.config.ts` + `e2e/evidence/`, excluded from the mocked smoke config
- [x] README pointer to bench usage (operator-facing, short)

Not yet exercised: no full two-arm run against a live provider has been recorded, so no `docs/evidence/bench-*.md` exists. Host spawn, health, metrics join, and summary composition were smoke-tested against the bench database without an upstream model.

## Design decisions (locked)

| Decision | Choice |
| --- | --- |
| Comparison | MAF compaction (default 256k) vs Comprexy Virtual + soft limit |
| MAF compaction window | Default `MaxContextWindowTokens = 256_000` on both arms; CLI-overridable; record resolved value in manifest |
| Who starts proxies | Harness (sequential) |
| Baseline metrics | `Mode=Off` + unreachable soft limit — not PassThrough |
| `comprexy` soft limit | Appsettings chain as loaded by the spawned host (no harness override); record resolved value in manifest |
| Config to children | Process env (`__`) + `--urls` |
| DB | Dedicated `data/comprexy-bench.db` by default; same file for proxies, control-api, report, screenshots |
| MAF tools | File + shell in v1; sandbox-rooted |
| Agent writes | Gitignored sandbox only |
| Token source of truth | Comprexy DB / control-api |
| Turn timing columns | In scope (Phase 0): `DurationMs`, `UpstreamDurationMs`, `PrepareDurationMs` |
| Analysis | Separate `report` step; MAF + MCP; deterministic numbers |
| Website | Curated `docs/evidence/` after review |
| Bench rows in product schema | No (timing columns on turns are product metrics, not bench run storage) |
| Screenshots | Optional live Playwright; mocked smokes unchanged |
