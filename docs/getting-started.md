# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS) — optional, for the metrics dashboard

## Clone and configure

```bash
git clone https://github.com/norielmallari/comprexy.git
cd comprexy
```

Configure your upstream in `apps/proxy/appsettings.json`, or copy the machine-local example for keys:

```bash
cp apps/proxy/appsettings.Local.json.example apps/proxy/appsettings.Local.json
```

Edit `appsettings.Local.json` with your upstream endpoint:

```json
{
  "Provider": {
    "BaseUrl": "http://localhost:11434/v1",
    "ApiKey": null,
    "Model": "your-model"
  }
}
```

Omit `Model` (or set it `null`) to forward the client's `model` field instead. Inline wrap-up and ToolSchema mapping then reuse that same client model unless `Compression:Model` is set.

## Run

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

Override the API base with `NEXT_PUBLIC_API_BASE_URL` if control-api is not on `http://localhost:8130`. Development CORS already allows `http://localhost:3000` in `apps/control-api/appsettings.Development.json` (and the Local example).

## Point a client

```text
Base URL:  http://localhost:8129/v1
API key:   any value, or omit (or Auth:RequiredApiKey if set)
```

Test with curl:

```bash
curl http://localhost:8129/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "client-model",
    "messages": [
      {"role": "system", "content": "You are a helpful coding assistant."},
      {"role": "user", "content": "Let''s build a REST API."}
    ]
  }'
```

## Token and cost intelligence

Comprexy OSS includes conversation-level token and cost intelligence for long-running workflows.

For each conversation, it tracks estimated baseline token volume, sent-equivalent token volume, compression overhead, net tokens saved, savings ratios, working-memory versions, budget events, and per-turn prompt growth. Cost figures are estimate-based: apply a USD-per-1M-token rate to those token totals when you want a cost-equivalent signal. These signals support workflow inspection and tuning; they do not guarantee savings or ROI.

## Design principles

- Answer first; fold on soft pressure when the stored tool chain is closed and cooldown allows.
- Persist the durable transcript; treat working memory as a derived, versioned prompt aid.
- Rebuild outgoing context from stored turns — do not forward an unmanaged client history as the model transcript.
- When Virtual Tools is on, own the model-facing tool contract: compact IR outbound, native remap to the client, distilled IR observations in the stored transcript.
- Extract client-attached IDE rules, inject pending rules as ephemeral system overlays, and fold standing rules into working memory on Inline accept — without re-evaluating client glob engines.
- Prefer inspectable, deterministic behavior over opaque truncation — tokens, benches, and evidence before guesswork.
- Stay OpenAI-compatible for local and frontier upstreams; stay narrow (context compression and tool-surface management, not a gateway or agent framework).

## MCP setup

Control-api exposes read-only conversation telemetry over MCP Streamable HTTP at `http://localhost:8130/mcp`.

For clients that use the common `mcpServers` JSON shape:

```json
{
  "mcpServers": {
    "comprexy-telemetry": {
      "url": "http://localhost:8130/mcp"
    }
  }
}
```

If `Auth:RequiredApiKey` is configured, add an HTTP header:

```json
{
  "mcpServers": {
    "comprexy-telemetry": {
      "url": "http://localhost:8130/mcp",
      "headers": {
        "Authorization": "Bearer <api-key>"
      }
    }
  }
}
```

Telemetry MCP tools are named `comprexy_*` and require `conversationId` from the proxy meta-tool `comprexy_get_current_conversation_id`.
