---
name: microsoft-agent-framework-dotnet
description: Build AI agents and multi-agent workflows in C# on .NET 10 with Microsoft Agent Framework (MAF) 1.x — Microsoft.Agents.AI, AIAgent, ChatClientAgent, AgentSession, tools, MCP, approvals, middleware, context providers, workflows, and ASP.NET Core hosting. Use when adding or reviewing MAF code, when the user mentions Microsoft Agent Framework, Microsoft.Agents.AI, AIAgent, AgentSession, AgentWorkflowBuilder, agent harness, or Foundry hosted agents, and when migrating from Semantic Kernel or AutoGen.
---

# Microsoft Agent Framework on .NET 10

Microsoft Agent Framework (MAF) reached 1.0 GA on 2026-04-02 and is the supported successor to Semantic Kernel agents and AutoGen. It builds directly on `Microsoft.Extensions.AI` (`IChatClient`), so MEAI types (`ChatMessage`, `ChatOptions`, `AITool`, `AIFunctionFactory`) are part of the public surface, not a separate world.

Packages target `netstandard2.0` / `net8.0`, so they restore cleanly on `net10.0`. Pin the latest `1.x`.

## Core model

Four types carry most of the work:

| Type | Role | Lifetime |
| --- | --- | --- |
| `AIAgent` | Stateless agent abstraction (instructions, tools, model) | Singleton — safe for DI, app lifetime |
| `AgentSession` | All per-conversation state (history, memories, tool state) | Per conversation; serialize to persist |
| `AgentResponse` / `AgentResponseUpdate` | Non-streaming and streaming results | Per run |
| `AIContextProvider` | Memory / context injection extension point | Shared across sessions — keep session state in the session |

The single most important invariant: **the agent holds no conversation state, the session holds all of it.** Never create an agent per request to isolate users; create a session per conversation instead.

## Getting started

```bash
dotnet add package Microsoft.Agents.AI
# Provider packages, pick what you need:
dotnet add package Microsoft.Agents.AI.Foundry   # Microsoft Foundry (AIProjectClient)
dotnet add package Microsoft.Agents.AI.OpenAI    # OpenAI / Azure OpenAI
dotnet add package Azure.Identity
```

```csharp
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(instructions: "You are a helpful assistant.", name: "Assistant");

AgentSession session = await agent.CreateSessionAsync();
AgentResponse response = await agent.RunAsync("What changed in the last deploy?", session);
```

`AsAIAgent` is an extension on `IChatClient` (and on provider clients such as `AIProjectClient`, where it also takes `model:`). It returns a `ChatClientAgent`.

Streaming uses the same shape:

```csharp
await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("Summarize the incident.", session))
{
    Console.Write(update);
}
```

Typed results come from the generic overload, which reads `response.Result`:

```csharp
AgentResponse<IncidentSummary> response = await agent.RunAsync<IncidentSummary>(prompt, session);
```

`DefaultAzureCredential` is fine for local development. In deployed code use a specific credential such as `ManagedIdentityCredential` to avoid credential probing latency and unintended fallback.

## Tools

Function tools are MEAI `AIFunction` instances. Describe parameters with `[Description]` — the attribute text becomes the schema the model sees, so it is behavior, not documentation.

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

[Description("Get the current queue depth for a worker pool.")]
static int GetQueueDepth([Description("Worker pool name.")] string pool) => Depths[pool];

AIAgent agent = chatClient.AsAIAgent(
    instructions: "You answer questions about worker pools.",
    tools: [AIFunctionFactory.Create(GetQueueDepth)]);
```

`AIFunctionFactory.Create` is reflection-based. If the host enables trimming or Native AOT, verify tool invocation under the published configuration rather than assuming it survives.

Per-run tools go through `ChatClientAgentRunOptions` instead of rebuilding the agent:

```csharp
var options = new ChatClientAgentRunOptions(new() { Tools = [AIFunctionFactory.Create(GetWeather)] });
AgentResponse response = await agent.RunAsync(prompt, session, options);
```

### MCP tools

MCP servers plug in through the official `ModelContextProtocol` package. `McpClientTool` implements `AITool`, so no adapter is needed.

```csharp
await using McpClient mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new()
    {
        Endpoint = new Uri("https://example.test/api/mcp"),
        Name = "Docs MCP",
        TransportMode = HttpTransportMode.StreamableHttp,
    }));

IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

AIAgent agent = chatClient.AsAIAgent(
    instructions: "You answer questions using the documentation tools.",
    tools: [.. mcpTools.Cast<AITool>()]);
```

Use `StdioClientTransport` for local server processes. Discover tools once at startup where the server allows it; `ListToolsAsync` on every request adds a round trip to each turn.

### Human approval

Wrap any tool with real-world side effects in `ApprovalRequiredAIFunction`. The run then completes with an approval request instead of a final answer, and the caller resumes it.

```csharp
AIFunction restart = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(RestartService));

AgentResponse response = await agent.RunAsync("Restart the ingest worker.", session);

foreach (FunctionApprovalRequestContent request in response.Messages
             .SelectMany(m => m.Contents)
             .OfType<FunctionApprovalRequestContent>())
{
    bool approved = await AskOperatorAsync(request.FunctionCall.Name);
    await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]), session);
}
```

Treat tool arguments as untrusted model output. Validate them before they reach a shell, a query, or a file path — approval gates intent, not payload.

## Sessions and persistence

Sessions round-trip through the agent, not through a separate history provider:

```csharp
JsonElement state = await agent.SerializeSessionAsync(session);
// ... store state keyed by conversation id ...
AgentSession restored = await agent.DeserializeSessionAsync(state);
```

Serialized session JSON contains the full conversation and may contain PII. Store it with access control and encryption at rest, and apply the same retention rules as any other conversation store.

For ASP.NET Core, implement `AgentSessionStore` and register it with `WithSessionStore` rather than hand-rolling a per-request agent factory. See [reference.md](reference.md) § Hosting.

## Middleware

Three interception points, all registered as delegates through the builder:

```csharp
AIAgent instrumented = agent
    .AsBuilder()
        .Use(runFunc: AuditRunAsync, runStreamingFunc: null)   // whole agent run
        .Use(ValidateToolCallAsync)                            // each function call
    .Build();
```

Chat-client middleware wraps the inference call itself and is composed on the `IChatClient` before the agent is created (or via the `clientFactory:` parameter on `AsAIAgent`).

Function-calling middleware only fires for agents backed by a `FunctionInvokingChatClient` — in practice `ChatClientAgent`. Registering it on a custom `AIAgent` silently does nothing. Always call `next` unless the intent is to block the call.

## Workflows

For anything beyond a single agent, use `Microsoft.Agents.AI.Workflows`. Prebuilt patterns:

```csharp
Workflow sequential = AgentWorkflowBuilder.BuildSequential(researcher, writer, editor);

Workflow handoff = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(triage)
    .WithHandoff(triage, billing)
    .WithHandoff(triage, support)
    .Build();
```

Execution is streaming-first and event-driven:

```csharp
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent output) { /* ... */ }
}
```

Workflows run in supersteps, which is what makes checkpointing and deterministic replay possible. Graph construction, fan-out/fan-in, checkpoints, and resume are in [reference.md](reference.md) § Workflows.

## Observability

MAF emits OpenTelemetry GenAI semantic-convention traces, metrics, and logs. Instrument both layers — the chat client sees model calls, the agent sees runs:

```csharp
IChatClient instrumented = chatClient.AsBuilder().UseOpenTelemetry(sourceName: SourceName).Build();
AIAgent agent = instrumented.AsAIAgent(instructions: "...").WithOpenTelemetry(sourceName: SourceName);
```

Register the source with `AddSource(SourceName)` on the tracer provider. `EnableSensitiveData = true` puts prompts and completions into spans — development and test only.

## Checklist for MAF changes

- [ ] Agents registered as singletons; no per-request agent construction
- [ ] Conversation state lives in `AgentSession`, persisted via a session store
- [ ] Tools have `[Description]` on the method and every parameter
- [ ] Side-effecting tools wrapped in `ApprovalRequiredAIFunction`, arguments validated after approval
- [ ] Non-development credentials are explicit, not `DefaultAzureCredential`
- [ ] Sensitive-data telemetry off outside development
- [ ] `dotnet build` clean

## In this repository

Comprexy targets `net10.0` with nullable enabled. MAF code here follows the existing conventions: constructor injection for every dependency, no `SaveChangesAsync` from Application leaf services, and no real paths, names, or log-harvested payloads in fixtures — author synthetic values instead.

## Additional resources

- [reference.md](reference.md) — workflows, ASP.NET Core hosting and protocols, agent harness, provider matrix, and the Semantic Kernel rename table
