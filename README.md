# Comprexy OSS

Apache-2.0 OpenAI-compatible **Comprehension Proxy** for context management, token observability, and reproducible agent benchmarks across local and frontier workflows.

**Comprexy OSS** sits between your client (Cursor, CLI agents, custom apps) and any OpenAI-compatible upstream — local or frontier. It makes long sessions workable through versioned working memory, soft-budget Inline wrap-up, Virtual Tools that replace heavy IDE tool catalogs with compact IR tools, and client rules management for Cursor/Kilo project rules. Full documentation: [docs site](https://norielmallari.github.io/comprexy/).

[Project direction](#project-direction) · [Quick start](#quick-start) · [Documentation](https://norielmallari.github.io/comprexy/) · [License](#license)

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-cross--platform-informational)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)
![Status](https://img.shields.io/badge/status-open%20core-informational)

## Project direction

This repository is the **Apache 2.0–licensed open core** of Comprexy OSS. Feature work, bug fixes, documentation, and compatibility improvements are welcome here under the [Apache License 2.0](LICENSE).

Further product work may also continue as **Comprexy**, separate from this repository. The Comprexy name and branding for separate or commercial products remain subject to the [Trademark](#trademark) terms below.

> Comprexy OSS is the open core. Comprexy is the product.

## Quick start

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download). Metrics dashboard also needs [Node.js](https://nodejs.org/) (LTS).

```bash
git clone https://github.com/norielmallari/comprexy.git
cd comprexy
```

Configure upstream in `apps/proxy/appsettings.json`, or copy `appsettings.Local.json.example` → `appsettings.Local.json` for machine-local settings (preferred for keys):

```json
{
  "Provider": {
    "BaseUrl": "http://localhost:11434/v1",
    "ApiKey": null,
    "Model": "your-model"
  }
}
```

Omit `Model` (or set it `null`) to forward the client's `model` field instead. Inline wrap-up and ToolSchema mapping then reuse that same client model unless `Compression:Model` is set (mapper only).

```bash
./comprexy.sh proxy          # data plane :8129
./comprexy.sh control-api    # metrics + MCP :8130
./comprexy.sh dev            # proxy + control-api (Ctrl-C stops both)
```

Windows (PowerShell or cmd):

```bat
.\comprexy.cmd proxy
.\comprexy.cmd control-api
.\comprexy.cmd dev
```

Metrics dashboard (optional UI over control-api; run in a second terminal after control-api is up):

```bash
cd apps/dashboard
npm install
npm run dev                  # http://localhost:3000
```

Override the API base with `NEXT_PUBLIC_API_BASE_URL` if control-api is not on `http://localhost:8130`. Development CORS already allows `http://localhost:3000` in `apps/control-api/appsettings.Development.json` (and the Local example); for other hosts, set `Cors:AllowedOrigins` on control-api.

If .NET 10 is missing, the script prompts to install the SDK into `~/.dotnet` (or `%USERPROFILE%\.dotnet` on Windows) via the official Microsoft install script. Use `install-dotnet` or `COMPREXY_AUTO_INSTALL_DOTNET=1` for non-interactive installs.

On first run, Comprexy OSS applies EF Core migrations and creates `data/comprexy.db` under the repo root (shared with control-api). Listen URLs:

| Process | URL |
| --- | --- |
| Proxy | `http://localhost:8129` (`/v1/chat/completions`, …) |
| Control-api | `http://localhost:8130` (e.g. `GET /v1/comprexy/conversations`, MCP at `/mcp`) |
| Dashboard | `http://localhost:3000` (browser UI; talks to control-api) |

Equivalent `dotnet run --project apps/proxy` / `apps/control-api` commands still work; `./comprexy.sh help` / `.\comprexy.cmd help` lists shortcuts (`test`, `build`, `clear-db`).

Point any OpenAI-compatible client at:

```text
Base URL:  http://localhost:8129/v1
API key:   any value, or omit (or Auth:RequiredApiKey if set)
```

```bash
curl http://localhost:8129/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "client-model",
    "messages": [
      {"role": "system", "content": "You are a helpful coding assistant."},
      {"role": "user", "content": "Let\'s build a REST API."}
    ]
  }'
```

On the normal path, when `Provider:Model` is set Comprexy OSS replaces `model` with that value; when it is null/omitted, the client's `model` is forwarded. In `Proxy:PassThrough` mode, the client body (including `model`) is forwarded as sent unless `Provider:Model` overrides it.

## Features

- **Observable tokens** — conversation- and turn-level metrics via control-api (dashboard, MCP)
- **Virtual Tools** — heavy IDE tool catalogs replaced with compact IR tools, remapped to native client calls
- **Client rules** — Cursor/Kilo attached rules extracted each turn, injected as ephemeral overlays, and folded into working memory on compression
- **Working memory** — versioned compressed context for bounded upstream prompts, folded on soft budget pressure
- **Benchmark harness** — reproducible compression comparison on real coding workloads

## License

[Apache License 2.0](LICENSE). See also [`NOTICE`](NOTICE).

## Copyright

Copyright 2026 Noriel Mallari. See [`NOTICE`](NOTICE).

## Trademark

Comprexy™ is a trademark claimed by Noriel Mallari.

The Apache License 2.0 applies to the software source code in this repository (Comprexy OSS). It does not grant permission to use the Comprexy name, logo, or branding to identify, market, or promote any separate, modified, or derivative product (see also Apache License §6).

Forks and derivatives should use a distinct name unless written permission is granted.

Descriptive attribution such as "based on Comprexy OSS" is allowed, provided it does not imply official endorsement, sponsorship, or affiliation.
