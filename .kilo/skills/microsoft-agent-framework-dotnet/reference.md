<!-- Generated from .cursor/skills/microsoft-agent-framework-dotnet/reference.md — edit the source, not this file. -->

# Microsoft Agent Framework .NET — reference

Detail behind [SKILL.md](SKILL.md). Read the section you need; this file is not meant to be loaded whole.

## Package map

| Package | Use for |
| --- | --- |
| `Microsoft.Agents.AI` | Core: `ChatClientAgent`, `AsAIAgent`, builders, OpenTelemetry |
| `Microsoft.Agents.AI.Abstractions` | `AIAgent`, `AgentSession`, `AgentResponse` — reference from libraries that must not pull the core |
| `Microsoft.Agents.AI.Workflows` | Graph workflows, orchestration patterns, checkpointing |
| `Microsoft.Agents.AI.Workflows.Declarative` | YAML-defined workflows |
| `Microsoft.Agents.AI.Workflows.Generators` | Roslyn source generators for compile-time executor routes |
| `Microsoft.Agents.AI.Hosting` | `AddAIAgent`, `AddWorkflow`, `AgentSessionStore` for `IHostApplicationBuilder` |
| `Microsoft.Agents.AI.Foundry` | Microsoft Foundry via `AIProjectClient` |
| `Microsoft.Agents.AI.Foundry.Hosting` | Foundry Hosted Agents (`AddFoundryResponses`) |
| `Microsoft.Agents.AI.OpenAI` / `.Anthropic` | Provider connectors |
| `Microsoft.Agents.AI.A2A`, `.Hosting.A2A.AspNetCore` | Agent-to-Agent protocol client and server |
| `Microsoft.Agents.AI.AGUI`, `.Hosting.AGUI.AspNetCore` | Agent-User Interaction protocol for web frontends |
| `Microsoft.Agents.AI.DurableTask` | Durable agents on Azure Functions or self-hosted Durable Task |
| `Microsoft.Agents.AI.Declarative` | YAML-defined agents |
| `Microsoft.Agents.AI.DevUI` | Local developer UI for inspecting runs |

`Microsoft.Agents.AI.AzureAI` is the former name of `Microsoft.Agents.AI.Foundry`; `PersistentAgentsClient` is obsolete in favor of `AIProjectClient`.

First-party connectors cover Microsoft Foundry, Azure OpenAI, OpenAI, Anthropic Claude, Amazon Bedrock, Google Gemini, Ollama, and the GitHub Copilot SDK. Any `IChatClient` works through `AsAIAgent`, so an unlisted provider only needs an MEAI client.

## Workflows

### Building a graph

`WorkflowBuilder` composes executors and edges. Passing an `AIAgent` wraps it in an agent executor automatically — do not construct the executor yourself.

```csharp
var workflow = new WorkflowBuilder(intake)
    .AddEdge(intake, classify)
    .AddEdge(classify, escalate, condition: msg => msg.Severity > 3)
    .AddFanOutEdge(classify, [enrichLogs, enrichMetrics])
    .AddFanInBarrierEdge([enrichLogs, enrichMetrics], report)
    .WithOutputFrom(report)
    .Build();
```

### Running

| Call | Behavior |
| --- | --- |
| `InProcessExecution.RunAsync(workflow, input)` | Non-streaming; events collected in `run.NewEvents` |
| `InProcessExecution.RunStreamingAsync(workflow, input)` | Streaming; iterate `run.WatchStreamAsync()` |
| `InProcessExecution.OpenStreamingAsync(workflow)` | Opens a run without input; the start executor waits for a message |
| `InProcessExecution.ResumeStreamingAsync(workflow, checkpointInfo, manager)` | Resumes from a checkpoint |

Events worth handling: `ExecutorCompletedEvent`, `AgentResponseUpdateEvent`, `RequestInfoEvent` (human-in-the-loop request port), `SuperStepCompletedEvent`, `WorkflowOutputEvent`, `WorkflowErrorEvent`.

### Supersteps and checkpoints

Execution advances in supersteps with a synchronization barrier: every executor scheduled in a step completes before the next step starts. That yields deterministic ordering and a consistent state boundary, which is exactly where checkpoints are taken.

```csharp
CheckpointManager manager = CheckpointManager.CreateInMemory();
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input, manager);

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is SuperStepCompletedEvent step)
    {
        CheckpointInfo? checkpoint = step.CompletionInfo?.Checkpoint;
    }
}
```

A checkpoint captures executor state, pending messages, pending requests and responses, and shared state. Agent executors additionally serialize their `AgentSession`. `CreateInMemory` is for a single process only — use a durable `CheckpointManager` for anything that must survive a restart.

### Orchestration patterns

`AgentWorkflowBuilder` provides the prebuilt topologies: sequential, concurrent, handoff, group chat, and Magentic. In the handoff pattern the framework injects the transfer tools; you declare the topology and the agents decide the routing.

## Hosting in ASP.NET Core

Register the chat client first, then agents against it.

```csharp
builder.Services.AddKeyedSingleton<IChatClient>("chat-model", chatClient);

builder.AddAIAgent("triage",
        instructions: "Route the request to the right specialist.",
        description: "Front-line triage agent.",
        chatClientServiceKey: "chat-model")
    .WithAITool(new QueueDepthTool())
    .WithSessionStore((sp, agentName) => new SqliteAgentSessionStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), agentName));
```

`AddAIAgent` returns an `IHostedAgentBuilder` and registers the agent as a keyed singleton under its name. `WithInMemorySessionStore()` is the development shortcut; a real store implements `AgentSessionStore` and persists the `JsonElement` from `SerializeSessionAsync` keyed by conversation id.

Endpoints then resolve both singletons — no factory, no per-request agent:

```csharp
app.MapPost("/triage/{conversationId}", async (
    string conversationId,
    [FromBody] Request input,
    [FromKeyedServices("triage")] AIAgent agent,
    [FromKeyedServices("triage")] AgentSessionStore sessions,
    CancellationToken ct) =>
{
    AgentSession session = await sessions.GetSessionAsync(agent, conversationId, ct);
    AgentResponse response = await agent.RunAsync(input.Text, session, ct);
    await sessions.SaveSessionAsync(agent, conversationId, session, ct);
    return Results.Ok(response.Text);
});
```

`conversationId` is an opaque string with no built-in tenancy. If sessions must be isolated per user, derive the key from the authenticated principal rather than accepting it from the request body.

### Workflows in DI

```csharp
var workflow = builder.AddWorkflow("intake", (sp, key) =>
{
    var triage = sp.GetRequiredKeyedService<AIAgent>("triage");
    var billing = sp.GetRequiredKeyedService<AIAgent>("billing");
    return AgentWorkflowBuilder.BuildSequential(key, [triage, billing]);
}).AddAsAIAgent();
```

Workflows have no protocol integrations of their own; `AddAsAIAgent()` is required before exposing one over A2A or an OpenAI-compatible endpoint.

### Protocol surfaces

| Surface | Wiring |
| --- | --- |
| A2A | `builder.Services.AddA2AServer();` then `app.MapA2AServer();` |
| OpenAI-compatible | `Microsoft.Agents.AI.Hosting.OpenAI` — Chat Completions / Responses shaped endpoints |
| AG-UI | `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` for browser frontends over HTTP + SSE |
| Foundry Hosted | `builder.Services.AddFoundryResponses(agent);` then `app.MapFoundryResponses();` |
| Durable | `Microsoft.Agents.AI.DurableTask` on Azure Functions or self-hosted compute |

Foundry Hosted Agents run your container on Foundry-managed infrastructure with scale-to-zero, per-session VM isolation, persistent filesystem across scale-down, and OpenTelemetry flowing into Application Insights without extra wiring.

## Context providers

`AIContextProvider` is the memory extension point. One instance serves every session, so instance fields may hold service clients but never session-scoped ids.

```csharp
AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = "You are a helpful assistant." },
    AIContextProviders = [new UserProfileMemory(memoryClient)],
});
```

Override `ProvideAIContextAsync` to return extra instructions, messages, or tools for the turn, and `StoreAIContextAsync` to extract and persist facts from new messages. Keep per-session values in the `AgentSession` — `ProviderSessionState<T>` exists for exactly that and rides along with session serialization.

## Agent harness

`AsHarnessAgent` turns a chat client into a coding-agent-style harness with the loop-management patterns already wired: automatic context compaction when the window fills, opinionated default instructions merged ahead of yours, and a set of providers.

```csharp
AIAgent agent = chatClient.AsHarnessAgent(maxContextWindowTokens, maxOutputTokens, new HarnessAgentOptions
{
    Name = "ReportAgent",
    Description = "Researches a topic and drafts a report.",
    FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(AppContext.BaseDirectory, "artifacts")),
    ChatOptions = new ChatOptions { Instructions = instructions, Tools = [new WebBrowsingTool()] },
});
```

Bundled providers: `FileMemoryProvider` (session-scoped notes), `FileAccessProvider`, `TodoProvider`, `AgentModeProvider` (plan vs execute), `AgentSkillsProvider` (filesystem skill discovery), `BackgroundAgentsProvider` (parallel child agents), hosted web search, and a sandboxed `ShellExecutor` on .NET. Storage is pluggable through `AgentFileStore`.

The harness grants filesystem and shell reach by default. Scope the file store to a dedicated directory and disable what a given agent does not need (for example `DisableWebSearch`) rather than accepting the full default surface.

## Migrating from Semantic Kernel or AutoGen

Preview and Semantic Kernel names that changed by 1.0:

| Old | Current |
| --- | --- |
| `AgentThread` | `AgentSession` |
| `GetNewThread()` | `CreateSessionAsync()` (now async) |
| `AgentRunResponse` | `AgentResponse` |
| `AgentRunResponseUpdate` | `AgentResponseUpdate` |
| `CreateAIAgent()` | `AsAIAgent()` |
| `Microsoft.Agents.AI.AzureAI` | `Microsoft.Agents.AI.Foundry` |
| `PersistentAgentsClient` | `AIProjectClient` |
| `AssistantClient.CreateAIAgentAsync()` | Removed — use the Responses API |
| `InProcessExecution.StreamAsync` | `RunStreamingAsync` |
| `AgentRunUpdateEvent` | `AgentResponseUpdateEvent` |
| `AIAgent.Id` (virtual) | `IdCore` (protected virtual) |

Custom `AIAgent` subclasses now override the `*Core` methods (`RunCoreAsync`, `CreateSessionCoreAsync`, and peers) rather than the public methods.

Semantic Kernel is not deleted, but new agent work belongs on MAF; the two can coexist during a migration through an adapter that wraps an SK agent as an `AIAgent`.

## Sources

- [Agent Framework documentation](https://learn.microsoft.com/agent-framework/)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework) — `dotnet/samples/` is the fastest way to confirm a current API shape
- [Agent Framework developer blog](https://devblogs.microsoft.com/agent-framework/)
