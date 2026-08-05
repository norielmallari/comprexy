# Contributing to Comprexy OSS

Thanks for contributing.

This repository is the **Apache 2.0–licensed open core** of Comprexy OSS. Features, bug fixes, documentation, and compatibility improvements are welcome. Further product work may also continue as Comprexy (separate from this tree); branding remains subject to the README [Trademark](https://github.com/norielmallari/comprexy/blob/main/README.md#trademark) terms. See [Project direction](https://github.com/norielmallari/comprexy/blob/main/README.md#project-direction). Contributions are accepted under the same [Apache License 2.0](https://github.com/norielmallari/comprexy/blob/main/LICENSE) (see Apache License §5).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build and test

```bash
./comprexy.sh build
./comprexy.sh test
```

Windows: `.\comprexy.cmd build` / `.\comprexy.cmd test` (thin `.cmd` shim → `comprexy.ps1`).

Or `dotnet build` / `dotnet test`. Local hosts: `proxy`, `control-api`, or `dev` via the same scripts.

## Project layout

```text
apps/
  proxy/                     # Data-plane host (Comprexy.Api), chat endpoints, prompts
  control-api/               # Control-plane host: metrics REST + telemetry MCP (`/mcp`)
  dashboard/                 # Optional Next.js metrics UI over control-api (`:3000`)

src/
  Comprexy.Domain/           Entities & enums (including tool catalog / dual-id map)
  Comprexy.Application/      Use cases, ports, orchestration (chat, Inline, ToolSchema / ToolIr)
  Comprexy.Infrastructure/   EF Core, HTTP client, tokenizer, background jobs, shared hosting

tests/
  Comprexy.Application.Tests/
  Comprexy.ControlApi.Tests/

docs/
  ARCHITECTURE.md            System map (chat lifecycle, Virtual Tools, client rules, persistence)
  SETTINGS.md                Operator config reference (including ToolSchema)
```

## Local database

On first run, the proxy (or control-api) applies EF Core migrations and creates `data/comprexy.db` under the repo root. Both hosts share that file by default. Override `ConnectionStrings:Comprexy` in `appsettings.Local.json` if you need a different absolute path.

Drop and recreate the database from migrations (deletes all data). Stop the proxy/control-api or any DB browser if the file is locked:

```bash
./comprexy.sh clear-db
# Windows: .\comprexy.cmd clear-db
# or: dotnet run --project apps/proxy -- --clear-db
```

`--clear-database` is accepted as an alias. `--clear-db` is proxy-only (not supported on control-api).

## EF Core migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Comprexy.Infrastructure/Comprexy.Infrastructure.csproj \
  --startup-project apps/proxy/Comprexy.Api.csproj \
  --output-dir Persistence/Migrations
```

Do not hand-author migration files; always use `dotnet ef migrations`.

## Local configuration

Copy the template and adjust for your machine (file is gitignored):

```bash
cp apps/proxy/appsettings.Local.json.example apps/proxy/appsettings.Local.json
# optional for control-api:
cp apps/control-api/appsettings.Local.json.example apps/control-api/appsettings.Local.json
```

Use Local for upstream `Provider` settings and optional `Trace:RequestFiles` audit logging. Omit keys you do not intend to override.

## CI

GitHub Actions builds on push to `main`. The workflow is defined in `.github/workflows/deploy.yml` and targets GitHub Pages.

## Benchmark harness

`tests/Comprexy.Bench` replays a frozen prompt list through a Microsoft Agent Framework coding agent twice — once with client-side compaction alone (`ToolSchema:Mode=Off`, unreachable soft limit) and once with Comprexy compression plus Virtual Tools — against harness-spawned proxy and control-api hosts on a dedicated `data/comprexy-bench.db`.

The default MAF client tool catalog is sized for IDE-comparable Off-arm `tools[]` weight (~15–16k cl100k tokens on compact OpenAI tool JSON): enriched file/shell schemas, denylist stubs whose names match stock `ToolSchema:ExcludeFromModelTools`, and a `Task` passthrough stub (not denylisted). WriteFile/EditFile stay real workspace backends. New `manifest.json` harness settings stamp `ClientToolCatalogVersion` (for example `ide-band-v1`). Lean evidence such as `docs/evidence/65f1b1b.md` was measured on the earlier 6-tool catalog and is **not** catalog-comparable to post-`ide-band-v1` runs.

```bash
./comprexy.sh bench run                              # spawn hosts, run both arms, write manifest.json
./comprexy.sh bench report --run-id <runId>          # join control-api metrics, draft summary.md
./comprexy.sh bench publish --run-id <runId> --confirm  # copy the reviewed summary to docs/evidence/
```

Each run writes to a gitignored directory named for the UTC minute it started, `reports/bench/20260801-1200/`, so a repeat never overwrites earlier artifacts. Only reviewed summaries are committed. Token numbers come from Comprexy's own turn metrics, so a run needs a configured provider and enough wall clock for two full passes.

By default, if the `maf-compact` arm fails with a provider/context error after X prompts (HTTP 502, completion stall, context overflow), the `comprexy` arm stops once it completes X+1 (`survived_baseline_failure`) instead of finishing the script. Opt out with `--continue-past-baseline-failure` (optional `--survival-margin <n>`). Heavier Off-arm tool catalogs can move that kill point earlier; do not weaken survival defaults to chase continuity with lean catalog evidence.

## Documentation

Public docs live in `README.md`, `CONTRIBUTING.md`, and `docs/`. Keep them factual and operator-facing: what the software does, how to configure it, and known limits. Prefer calm, precise language over marketing or audit-style severity writeups.

Design notes, private reviews, and internal backlog should stay out of the public tree (e.g. gitignored `internal/`). Use GitHub Issues when an item needs public discussion.

## Security

- Do **not** commit real API keys or `appsettings.Local.json`. Prefer Local overrides, environment variables, or user secrets for `Provider:ApiKey`, `Compression:ApiKey`, and `Auth:RequiredApiKey`.
- Request audit files under `logs/requests/` can contain full prompts, tool arguments, paths, and completions. Keep them out of git (already gitignored), PRs, tickets, and shared screenshots.
- Do not paste live secrets or production request logs into issues or discussions.

## AI-assisted development

Most of this repository — application code, tests, and documentation — was produced with AI coding assistants under human direction. Maintainers review and remain responsible for what ships.

Treat PRs, issues, and docs the same as any other project: assume the material needs the same scrutiny you would give human-authored work. When using assistants yourself, keep changes focused, verify build and tests, and do not commit secrets or local request logs.

## Pull requests

1. Branch from `main` / `master`.
2. Keep changes focused; match existing style.
3. Run `dotnet build` and `dotnet test` before opening a PR.
4. Follow [Security](#security) — no secrets, Local overrides, or request-log contents in the PR.
5. Update public docs when behavior or configuration changes.
