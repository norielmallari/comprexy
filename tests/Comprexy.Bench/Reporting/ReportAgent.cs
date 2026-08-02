using System.ClientModel;
using System.Text;
using System.Text.Json;
using Comprexy.Bench.Cli;
using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Writes the interpretation prose for <c>summary.md</c>. It receives the deterministic numbers
/// block as input and may drill into the run with the bench control-api's telemetry MCP tools, but
/// it never produces a figure of its own.
/// </summary>
internal sealed class ReportAgent(BenchOptions options, string controlApiBaseUrl)
{
    private const string BaseInstructions = """
        You write the interpretation section of a Comprexy benchmark report for the project's own
        docs. You are given a deterministic numbers block that was computed in code, and telemetry
        tools for the same run.

        Rules:
        - Quote only figures that appear in the numbers block or that a tool returned. Never
          estimate, round differently, or extrapolate a number.
        - Write two to four short paragraphs of plain prose. No headings, no bullet lists, no table.
        - Output ONLY those final paragraphs. Do not include plans, critiques, checklists,
          scratch notes, or XML/thinking tags — the response body is pasted into summary.md as-is.
        - Say what the run shows and what it does not. A single local run on one model is not a
          general benchmark; say so plainly rather than hedging vaguely.
        - If the treatment arm's client-side compaction fired, name it as a limit on the result.
        - Outcome status `survived_baseline_failure` is intentional harness early-stop: after
          maf-compact died of a provider/context failure on prompt X (having completed X-1),
          comprexy stopped once it completed past that kill zone (default stop at prompt X).
          Treat that as a survival / kill-zone result, not as a crash and not as a full-script
          token pair.
        - When the numbers block includes a "Common completed prefix" table, that is the fair token
          and wall-clock comparison for survival runs: prompts 1..X-1 on both arms. Quote those
          sent / saved / reduction / peak / wall clock figures. Do not use full-run treatment
          wall clock or totals against a shorter baseline conversation, and do not invent prefix
          figures the block does not list. Token figures in the numbers block use provider-actual
          prompt basis (usage.prompt_tokens when present) on both arms — do not re-derive from
          tiktoken estimates in tool output if the numbers block already states the basis.
        - Status `failed` with an operator-abort reason is different from `survived_baseline_failure`;
          only the latter is the harness's first-class survival outcome.
        - No marketing language, no severity labels, no local file paths, no request-log content.
        """;

    public async Task<string> WriteInterpretationAsync(
        BenchMetrics metrics,
        string numbersBlock,
        CancellationToken cancellationToken)
    {
        // The report talks to the provider directly: routing it through a bench proxy would add a
        // report conversation to the same database the report is measuring.
        var (providerBaseUrl, providerApiKey, providerModel) =
            HostConfigurationResolver.ResolveProvider("Development");

        var endpoint = providerBaseUrl
            ?? throw new BenchUsageException(
                "Provider:BaseUrl is not configured, so the report agent has no upstream endpoint. Use --no-agent.");

        var model = options.Model
            ?? providerModel
            ?? throw new BenchUsageException(
                "No model to send: pass --model, or run report with --no-agent.");

        await using var mcpClient = await TryConnectMcpAsync(cancellationToken);
        var tools = mcpClient is null
            ? []
            : (await mcpClient.ListToolsAsync(cancellationToken: cancellationToken))
                .Cast<AITool>()
                .ToList();

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(options.CompletionTimeoutSeconds)
        };

        var agent = new OpenAIClient(
                new ApiKeyCredential(string.IsNullOrEmpty(providerApiKey) ? "comprexy-bench" : providerApiKey),
                clientOptions)
            .GetChatClient(model)
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "comprexy-bench-report",
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildInstructions(),
                    Tools = tools,
                    Temperature = 0f,
                    MaxOutputTokens = options.MaxOutputTokens
                }
            });

        var response = await agent.RunAsync(BuildPrompt(metrics, numbersBlock), cancellationToken: cancellationToken);
        return response.Text;
    }

    private static string BuildInstructions()
    {
        var builder = new StringBuilder(BaseInstructions);
        var tonePath = Path.Combine(BenchPaths.RepoRoot, ".cursor", "rules", "documentation-tone.mdc");
        if (File.Exists(tonePath))
        {
            builder.AppendLine().AppendLine().AppendLine("Repository documentation tone:").AppendLine();
            builder.AppendLine(File.ReadAllText(tonePath));
        }

        return builder.ToString();
    }

    private static string BuildPrompt(BenchMetrics metrics, string numbersBlock)
    {
        var conversationIds = new List<object>();
        foreach (var pair in metrics.Paired)
        {
            conversationIds.Add(new
            {
                pair.Name,
                Kind = "paired",
                MafCompactConversationId = pair.MafCompact.ConversationId,
                ComprexyConversationId = pair.Comprexy.ConversationId
            });
        }

        foreach (var survival in metrics.Survivals)
        {
            if (survival.MafCompact is null && survival.Comprexy is null)
            {
                continue;
            }

            conversationIds.Add(new
            {
                survival.Name,
                Kind = "survival",
                MafCompactConversationId = survival.MafCompact?.ConversationId,
                ComprexyConversationId = survival.Comprexy?.ConversationId
            });
        }

        return $"""
            Deterministic numbers block for this run:

            {numbersBlock}

            Conversation ids (paired full-script and survival early-stop), if you want to inspect
            a run with the telemetry tools:

            {JsonSerializer.Serialize(conversationIds, BenchJson.Options)}

            Write the interpretation section now. Reply with only the final two to four paragraphs.
            """;
    }

    private async Task<McpClient?> TryConnectMcpAsync(CancellationToken cancellationToken)
    {
        try
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{controlApiBaseUrl}/mcp"),
                Name = "comprexy-bench-telemetry",
                TransportMode = HttpTransportMode.StreamableHttp
            };

            var controlApiKey = HostConfigurationResolver.ResolveControlApiKey("Development");
            if (!string.IsNullOrWhiteSpace(controlApiKey))
            {
                transportOptions.AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {controlApiKey}"
                };
            }

            return await McpClient.CreateAsync(
                new HttpClientTransport(transportOptions),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine(
                $"warning: telemetry MCP unavailable ({ex.Message}); the report agent will work from the numbers block only.");
            return null;
        }
    }
}
