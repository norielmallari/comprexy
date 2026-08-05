---
name: bench-harness
description: >-
  Comprexy compression benchmark harness reference — CLI (`./comprexy.sh bench`),
  conversation scripts, survival early-stop, report/publish flow, and artifact
  paths under reports/bench/. Use when running or debugging benchmarks, writing
  bench-runner handoffs, or explaining harness flags.
---

# Bench harness

`tests/Comprexy.Bench` replays frozen prompt lists through a MAF coding agent twice — `maf-compact` (client compaction, `ToolSchema:Off`) then `comprexy` (Virtual Tools + soft budget) — against spawned proxy/control-api hosts and `data/comprexy-bench.db`.

Default client catalog (`SandboxToolCatalog`, version `ide-band-v1`) is IDE-comparable: ~15–16k cl100k tokens on compact OpenAI `tools[]` (CI band 14_500–16_500), including denylist stubs matching stock `ExcludeFromModelTools` and a non-denylisted `Task` stub. New manifests stamp `Harness.ClientToolCatalogVersion` (optional `ClientToolCatalogTokens`). Lean evidence `docs/evidence/65f1b1b.md` used the prior 6-tool catalog and is not catalog-comparable.

Operator docs: `docs/contributing.md` (Benchmark harness), `docs/SETTINGS.md` (`BenchOrchestration`, `ToolSchema:ExcludeFromModelTools`).

## Commands

```bash
./comprexy.sh bench run [options]       # spawn hosts, run arms, write manifest.json
./comprexy.sh bench report --run-id <id> [--no-agent] [--screenshots]
./comprexy.sh bench publish --run-id <id> --confirm   # human-reviewed only
./comprexy.sh bench help
```

### One script at a time (preferred)

```bash
./comprexy.sh bench run --run-id heavy-tool-usage --conversation heavy-tool-usage
./comprexy.sh bench report --run-id <exact-stamp-heavy-tool-usage> --no-agent
```

`--run-id` on **run** is a **label** appended to a UTC `yyyyMMdd-HHmm` stamp (unless `--exact-run-id`). On **report** / **publish**, pass the **exact directory name** under `reports/bench/`.

## Conversation scripts

Dir: `tests/Comprexy.Bench.Conversations/*.json`

| Name | Role |
| --- | --- |
| `smoke-large-blob` | Fast smoke |
| `heavy-tool-usage` | Heavy tools |
| `mixed-workload` | Mixed |
| `short-deep` | Many short prompts |
| `edge-case-noisy` | Noisy edge cases |
| `long-planning` | Long planning |

Empty `--conversation` = all scripts (avoid for agent runs). Repeat `--conversation` only when the human wants a multi-script single process.

Default agent queue order: smoke → heavy-tool-usage → mixed-workload → short-deep → edge-case-noisy → long-planning.

## Important flags

| Flag | Notes |
| --- | --- |
| `--conversation <name>` | Restrict scripts (repeatable) |
| `--arm maf-compact\|comprexy` | Restrict arms (repeatable; default both, sequential) |
| `--model <name>` | Else proxy `Provider:Model` |
| `--seed <n>` / `--no-seed` | Default seed `7` |
| `--skip-build` | Reuse host build after first spawn |
| `--no-spawn` | Attach to already-running hosts |
| `--trace` | Per-arm request traces under the run dir |
| `--continue-past-baseline-failure` | Disable survival early-stop |
| `--survival-margin <n>` | Extra prompts past baseline kill (default 1) |
| `--conversation-timeout <s>` | Default 7200 |
| `--no-agent` | Report: figures only, no MAF narrative |

## Survival early-stop

Default **on**: if `maf-compact` dies of provider/context failure after X prompts, `comprexy` stops at X+margin (`survived_baseline_failure`) instead of finishing the script. That is an intentional outcome class, not a harness bug.

Paired token headlines require **completed** on both arms. Survivals are reported separately; do not mix them into the paired savings claim.

## Artifacts

| Path | Writer |
| --- | --- |
| `reports/bench/<runId>/manifest.json` | `bench run` |
| `reports/bench/<runId>/metrics.json` | `bench report` |
| `reports/bench/<runId>/summary.md` | `bench report` |
| `docs/evidence/<id>.md` | `bench publish --confirm` only |

Runs are gitignored. Concurrent CLI + dashboard writers under `reports/bench/` are blocked by the shared `.active-run.lock` (CLI acquires it on `bench run`; dashboard orchestrator holds it and passes `--under-orchestrator-lock` to the child). Spawn also refuses when ports `18129` / `18130` / `18131` (or overrides) are already bound. Stale locks whose recorded pid is dead are reclaimed.

## Agent policy pointers

- Prefer **one `--conversation` per run**
- **Continue the queue on per-script errors**; record fail and proceed
- Never invent token counts — join via `bench report`
- Never `publish` without human review + `--confirm`
