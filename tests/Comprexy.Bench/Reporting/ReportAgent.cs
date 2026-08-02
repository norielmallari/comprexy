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
        - Say what the run shows and what it does not. A single local run on one model is not a
          general benchmark; say so plainly rather than hedging vaguely.
        - If the treatment arm's client-side compaction fired, name it as a limit on the result.
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
        var conversationIds = metrics.Paired.Select(p => new
        {
            p.Name,
            MafCompactConversationId = p.MafCompact.ConversationId,
            ComprexyConversationId = p.Comprexy.ConversationId
        });

        return $"""
            Deterministic numbers block for this run:

            {numbersBlock}

            Paired conversation ids, if you want to inspect a run with the telemetry tools:

            {JsonSerializer.Serialize(conversationIds, BenchJson.Options)}

            Write the interpretation section now.
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
