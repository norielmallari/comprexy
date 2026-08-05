---
title: Home
---

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-cross--platform-informational)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)
![Status](https://img.shields.io/badge/status-open%20core-informational)

Comprexy OSS is an Apache-2.0 licensed **OpenAI-compatible Comprehension Proxy** for context management, client rules handling, token observability, and reproducible agent benchmarks across local and frontier workflows.

It sits between your client (Cursor, CLI agents, custom apps) and any OpenAI-compatible upstream — local or frontier — making long sessions workable. Conversation- and turn-level metrics are available via control-api, the optional dashboard, and telemetry MCP; the benchmark harness and published dogfood evidence support comparing compression setups on real coding workloads; and the same signals can inform when a local model is enough versus when to use a frontier endpoint.

Mechanically, it persists completed turns, rebuilds a bounded upstream prompt from versioned working memory plus still-unfolded messages, manages client-attached IDE rules as ephemeral overlays that fold into working memory on compression, and folds older context via Inline wrap-up when soft budget pressure applies — without summarizing on every reply.

## Capabilities

- **Virtual Tools** — compact IR tools on the model path; native remap to the client
- **Client rules** — extract Cursor/Kilo attached rules each turn; inject pending overlays; fold standing rules into working memory `## Rules` on Inline accept
- **Working memory** — versioned compressed context with a tip retain window under soft budget pressure
- **Token observability** — control-api metrics, optional dashboard, telemetry MCP
- **Benchmark harness** — reproducible two-arm comparisons and published dogfood evidence

## Quick start

```bash
git clone https://github.com/norielmallari/comprexy.git
cd comprexy
./comprexy.sh dev
```

Point any OpenAI-compatible client at `http://localhost:8129/v1`. Full configuration and setup details are in [Getting Started](getting-started.md).

## What's next

- [Getting Started](getting-started.md) — prerequisites, configuration, first conversation
- [Benchmarks](benchmarks.md) — dogfood evidence and benchmark comparison results
- [Dashboard](dashboard.md) — optional metrics UI over control-api
- [Architecture](ARCHITECTURE.md) — layering, request lifecycle, Virtual Tools, client rules
- [Configuration](SETTINGS.md) — full settings reference
- [FAQ](faq.md) — common questions, limitations, what Comprexy OSS is not
