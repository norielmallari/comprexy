using System.Net;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Retrieval;
using Comprexy.Application.Models.Telemetry;
using Comprexy.ControlApi.Configuration;
using Comprexy.ControlApi.Endpoints;
using Comprexy.ControlApi.Mcp;
using Comprexy.ControlApi.Mcp.Resources;
using Comprexy.ControlApi.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace Comprexy.ControlApi.Tests;

public sealed class McpTransportTests
{
    [Fact]
    public async Task StreamableHttp_InitializesListsAndInvokesAllToolsAndResources()
    {
        var conversationId = Guid.NewGuid();
        var otherConversationId = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(conversationId, otherConversationId);
        var retrieval = McpTestData.CreateRetrieval(conversationId, otherConversationId);
        using var server = CreateServer(metrics.Object, retrieval.Object);
        using var httpClient = server.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await httpClient.GetAsync("/health")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await httpClient.GetAsync("/v1/comprexy/conversations")).StatusCode);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            McpTestData.ToolNames.Order(),
            tools.Select(tool => tool.Name).Order());
        foreach (var tool in tools)
        {
            IReadOnlyDictionary<string, object?>? arguments = tool.Name switch
            {
                "comprexy_get_conversation_summary"
                    or "comprexy_get_conversation_turns"
                    or "comprexy_get_final_turn_snapshot"
                    or "comprexy_get_compression_phase_breakdown"
                    or "comprexy_get_budget_events"
                    or "comprexy_get_evidence_markdown"
                    or "comprexy_get_prompt_growth_timeline"
                    or "comprexy_get_working_memory"
                    or "comprexy_get_recent_messages"
                    or "comprexy_get_open_tool_chains" =>
                    new Dictionary<string, object?> { ["conversationId"] = conversationId },
                "comprexy_search_conversation" => new Dictionary<string, object?>
                {
                    ["conversationId"] = conversationId,
                    ["query"] = "fingerprint"
                },
                "comprexy_get_message_window" => new Dictionary<string, object?>
                {
                    ["conversationId"] = conversationId,
                    ["sequenceStart"] = 0,
                    ["sequenceEnd"] = 2
                },
                "comprexy_compare_conversations" => new Dictionary<string, object?>
                {
                    ["leftConversationId"] = conversationId,
                    ["rightConversationId"] = otherConversationId
                },
                _ => null
            };

            var result = await client.CallToolAsync(tool.Name, arguments);
            Assert.NotEqual(true, result.IsError);
            Assert.NotEmpty(result.Content.OfType<TextContentBlock>());
        }

        var resources = await client.ListResourcesAsync();
        var templates = await client.ListResourceTemplatesAsync();

        Assert.Empty(resources);
        Assert.Equal(McpTestData.ResourceTemplateCount, templates.Count);
        var resourceUris = McpTestData.ResourceUrisFor(conversationId);
        foreach (var uri in resourceUris)
        {
            var result = await client.ReadResourceAsync(uri);
            Assert.NotEmpty(result.Contents.OfType<TextResourceContents>());
        }
    }

    private static TestServer CreateServer(
        IConversationMetricsQueryService metrics,
        IConversationRetrievalQueryService retrieval)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddHttpContextAccessor();
                services.AddSingleton(metrics);
                services.AddSingleton(retrieval);
                services.Configure<McpTelemetryOptions>(_ => { });
                services.AddSingleton<McpToolCallAuditLogger>();
                services.AddMcpServer()
                    .WithHttpTransport(options => options.Stateless = true)
                    .WithTools<ConversationTools>()
                    .WithTools<ConversationRetrievalTools>()
                    .WithResources<ConversationResources>()
                    .WithResources<ConversationRetrievalResources>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHealthEndpoints();
                    endpoints.MapMetricsEndpoints();
                    endpoints.MapMcp("/mcp");
                });
            });
        return new TestServer(builder);
    }
}

internal static class McpTestData
{
    public static readonly string[] ToolNames =
    [
        "comprexy_get_conversation_summary",
        "comprexy_get_conversation_turns",
        "comprexy_get_final_turn_snapshot",
        "comprexy_get_compression_phase_breakdown",
        "comprexy_get_budget_events",
        "comprexy_get_evidence_markdown",
        "comprexy_get_prompt_growth_timeline",
        "comprexy_compare_conversations",
        "comprexy_search_conversation",
        "comprexy_get_message_window",
        "comprexy_get_recent_messages",
        "comprexy_get_working_memory",
        "comprexy_get_open_tool_chains"
    ];

    public const int ResourceTemplateCount = 10;

    public static IReadOnlyList<string> ResourceUrisFor(Guid conversationId) =>
    [
        $"comprexy://conversation/{conversationId}/summary",
        $"comprexy://conversation/{conversationId}/turns",
        $"comprexy://conversation/{conversationId}/phases",
        $"comprexy://conversation/{conversationId}/final-turn",
        $"comprexy://conversation/{conversationId}/budget-events",
        $"comprexy://conversation/{conversationId}/evidence",
        $"comprexy://conversation/{conversationId}/prompt-growth-timeline",
        $"comprexy://conversation/{conversationId}/working-memory",
        $"comprexy://conversation/{conversationId}/recent-messages",
        $"comprexy://conversation/{conversationId}/open-tool-chains"
    ];

    public static Mock<IConversationMetricsQueryService> CreateMetrics(params Guid[] conversationIds)
    {
        var mock = new Mock<IConversationMetricsQueryService>();
        mock.Setup(x => x.ListConversationSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        mock.Setup(x => x.ConversationExistsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => conversationIds.Contains(id));
        mock.Setup(x => x.GetTelemetrySummaryAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, int? _, CancellationToken _) => Summary(id));
        mock.Setup(x => x.GetTelemetryTurnsAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ConversationTurnDto { TurnIndex = 1 }]);
        mock.Setup(x => x.GetFinalTurnSnapshotAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new FinalTurnSnapshotDto { ConversationId = id, TurnIndex = 1 });
        mock.Setup(x => x.GetPhaseBreakdownAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ConversationPhaseDto { Phase = "working_memory_v1" }]);
        mock.Setup(x => x.GetBudgetEventsAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, int? _, CancellationToken _) =>
                new ConversationBudgetEventDto { ConversationId = id });
        mock.Setup(x => x.GetEvidenceMarkdownAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("## Evidence");
        mock.Setup(x => x.GetPromptGrowthTimelineAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, int? _, CancellationToken _) =>
                new PromptGrowthTimelineDto { ConversationId = id });
        mock.Setup(x => x.CompareConversationsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid left, Guid right, int? _, CancellationToken _) =>
                new ConversationComparisonDto { Left = Summary(left), Right = Summary(right) });
        return mock;
    }

    public static Mock<IConversationRetrievalQueryService> CreateRetrieval(params Guid[] conversationIds)
    {
        var mock = new Mock<IConversationRetrievalQueryService>();
        mock.Setup(x => x.ConversationExistsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => conversationIds.Contains(id));
        mock.Setup(x => x.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, string query, int? _, bool _, bool _, CancellationToken _) =>
                new ConversationSearchResultDto
                {
                    ConversationId = id,
                    Query = query,
                    Matches =
                    [
                        new ConversationSearchMatchDto
                        {
                            SourceType = "message",
                            Sequence = 1,
                            Role = "user",
                            Text = "match"
                        }
                    ]
                });
        mock.Setup(x => x.GetMessageWindowAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ConversationMessageSnippetDto
                {
                    Sequence = 0,
                    Role = "user",
                    Text = "hello"
                }
            ]);
        mock.Setup(x => x.GetRecentMessagesAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ConversationMessageSnippetDto
                {
                    Sequence = 1,
                    Role = "assistant",
                    Text = "hi"
                }
            ]);
        mock.Setup(x => x.GetWorkingMemoryAsync(
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, int? version, CancellationToken _) =>
                new WorkingMemorySnapshotDto
                {
                    ConversationId = id,
                    Version = version ?? 1,
                    Content = "wm",
                    TokenCount = 3,
                    CreatedAt = DateTimeOffset.UnixEpoch
                });
        mock.Setup(x => x.GetOpenToolChainsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new OpenToolChainsDto
                {
                    ConversationId = id,
                    IsOpen = false,
                    UnmatchedCount = 0,
                    OpenToolCallIds = []
                });
        return mock;
    }

    public static ConversationSummaryDto Summary(Guid id) =>
        new() { ConversationId = id, TurnCount = 1 };
}
