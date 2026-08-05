# Installation

## .NET 10 SDK

Comprexy OSS requires [.NET 10 SDK](https://dotnet.microsoft.com/download). The provided scripts will prompt to install it if missing:

```bash
./comprexy.sh install-dotnet
# Non-interactive: COMPREXY_AUTO_INSTALL_DOTNET=1 ./comprexy.sh install-dotnet
```

The SDK installs to `~/.dotnet` (or `$DOTNET_ROOT` if set). On Windows, `%USERPROFILE%\.dotnet` (or `$env:DOTNET_ROOT`).

## Quick start commands

| Command | Description |
| --- | --- |
| `./comprexy.sh dev` | Run proxy + control-api together |
| `./comprexy.sh proxy` | Run the data plane only (`:8129`) |
| `./comprexy.sh control-api` | Run the control plane only (`:8130`) |
| `./comprexy.sh build` | Build all projects |
| `./comprexy.sh test` | Run the test suite |
| `./comprexy.sh clear-db` | Drop and recreate the SQLite database |
| `./comprexy.sh bench run` | Run the benchmark harness |
| `./comprexy.sh help` | List all available commands |

Windows equivalents use `.\comprexy.cmd` instead of `./comprexy.sh`.

## Dashboard

The optional metrics dashboard is a Next.js application in `apps/dashboard/`:

```bash
cd apps/dashboard
npm install
npm run dev                  # http://localhost:3000
```

The dashboard talks to control-api at `http://localhost:8130` by default. Override with `NEXT_PUBLIC_API_BASE_URL`.

## Windows notes

- Use `.\comprexy.cmd` (thin shim → `comprexy.ps1`) or run PowerShell directly.
- Execution policy: `.\comprexy.cmd` passes `-ExecutionPolicy Bypass` automatically.
- The `.cmd` file contains no business logic — it delegates to `.ps1`.

## Listen URLs

| Process | URL |
| --- | --- |
| Proxy | `http://localhost:8129` (`/v1/chat/completions`, …) |
| Control-api | `http://localhost:8130` (REST + MCP at `/mcp`) |
| Dashboard | `http://localhost:3000` (browser UI) |

## Local database

On first run, the proxy (or control-api) applies EF Core migrations and creates `data/comprexy.db` under the repo root. Both hosts share that file by default. Override `ConnectionStrings:Comprexy` in `appsettings.Local.json` for a different path.
