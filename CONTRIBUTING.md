# Contributing to Comprexy

Thanks for contributing.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build and test

```bash
dotnet build
dotnet test
```

## Project layout

```text
src/
  Comprexy.Domain/           Entities & enums
  Comprexy.Application/      Use cases, ports, orchestration
  Comprexy.Infrastructure/   EF Core, HTTP client, tokenizer, background jobs
  Comprexy.Api/              Minimal API, DTOs, composition root

tests/
  Comprexy.Application.Tests/

docs/
  ARCHITECTURE.md            System map for contributors / agents
  TODO.md                    Public backlog / deferred work
```

## Local database

On first run, the API applies EF Core migrations and creates `comprexy.db` next to the API project.

Drop and recreate the database from migrations (deletes all data). Stop the API or any DB browser if the file is locked:

```bash
dotnet run --project src/Comprexy.Api -- --clear-db
```

`--clear-database` is accepted as an alias.

## EF Core migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Comprexy.Infrastructure/Comprexy.Infrastructure.csproj \
  --startup-project src/Comprexy.Api/Comprexy.Api.csproj \
  --output-dir Persistence/Migrations
```

Do not hand-author migration files; always use `dotnet ef migrations`.

## Local configuration

Copy the template and adjust for your machine (file is gitignored):

```bash
cp src/Comprexy.Api/appsettings.Local.json.example src/Comprexy.Api/appsettings.Local.json
```

Use Local for upstream `Provider` settings and optional `Trace:RequestFiles` audit logging. Omit keys you do not intend to override.

## Documentation

Public docs live in `README.md`, `CONTRIBUTING.md`, and `docs/`. Keep them factual and operator-facing: what the software does, how to configure it, and known limits. Prefer calm, precise language over marketing or audit-style severity writeups.

Deferred work belongs in [`docs/TODO.md`](docs/TODO.md) or GitHub Issues. Design notes and private reviews should stay out of the public tree.

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
