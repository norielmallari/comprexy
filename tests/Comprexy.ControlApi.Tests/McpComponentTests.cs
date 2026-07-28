using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models.Retrieval;
using Comprexy.Application.Models.Telemetry;
using Comprexy.ControlApi.Configuration;
using Comprexy.ControlApi.Mcp;
using Comprexy.ControlApi.Mcp.Resources;
using Comprexy.ControlApi.Mcp.Tools;
using Comprexy.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.ControlApi.Tests;

public sealed class McpToolAndResourceTests
{
    [Fact]
    public async Task ExplicitTool_UnknownConversationReturnsIsErrorPayload()
    {
        var metrics = McpTestData.CreateMetrics();
        var accessor = new HttpContextAccessor();
        var tool = new ConversationTools(
            metrics.Object,
            new McpToolCallAuditLogger(new CapturingLogger<McpToolCallAuditLogger>()),
            Options.Create(new McpTelemetryOptions()),
            accessor);

        var unknown = await tool.GetConversationSummaryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(JsonDocument.Parse(unknown).RootElement.GetProperty("isError").GetBoolean());
        Assert.Contains("Conversation not found", unknown);
    }

    [Fact]
    public async Task ExplicitTool_MapsLinkedQueryCancellationToTimeoutError()
    {
        var id = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(id);
        metrics.Setup(x => x.GetTelemetrySummaryAsync(
                id,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Guid _, int? _, CancellationToken token) =>
            {
                Assert.True(token.CanBeCanceled);
                throw new OperationCanceledException(token);
            });
        var accessor = new HttpContextAccessor();
        var tool = new ConversationTools(
            metrics.Object,
            new McpToolCallAuditLogger(new CapturingLogger<McpToolCallAuditLogger>()),
            Options.Create(new McpTelemetryOptions()),
            accessor);

        var result = await tool.GetConversationSummaryAsync(id, CancellationToken.None);

        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("isError").GetBoolean());
        Assert.Contains("Telemetry query timed out.", result);
    }

    [Fact]
    public async Task ExplicitResource_MapsQueryExceptionToErrorPayload()
    {
        var id = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(id);
        metrics.Setup(x => x.GetTelemetrySummaryAsync(
                id,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));
        var resource = new ConversationResources(
            metrics.Object,
            Options.Create(new McpTelemetryOptions()));

        var result = await resource.GetSummaryAsync(id, CancellationToken.None);

        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("isError").GetBoolean());
        Assert.Contains("Telemetry query failed: db unavailable", result);
    }

    [Fact]
    public async Task ExplicitTurns_ClampsRowsAndEmitsStructuredAuditLog()
    {
        var id = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(id);
        int? observedTake = null;
        metrics.Setup(x => x.GetTelemetryTurnsAsync(
                id,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid _, int? take, CancellationToken _) => observedTake = take)
            .ReturnsAsync([new ConversationTurnDto { TurnIndex = 1 }]);
        var logger = new CapturingLogger<McpToolCallAuditLogger>();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        var tools = new ConversationTools(
            metrics.Object,
            new McpToolCallAuditLogger(logger),
            Options.Create(new McpTelemetryOptions
            {
                DefaultRowLimit = 200,
                MaxRowLimit = 7,
                QueryTimeoutSeconds = 5
            }),
            accessor);

        var result = await tools.GetConversationTurnsAsync(id, CancellationToken.None);

        Assert.Equal(7, observedTake);
        Assert.Contains("\"turnIndex\":1", result);
        var audit = Assert.Single(logger.Logs);
        Assert.Equal("comprexy_get_conversation_turns", audit.Properties["ToolName"]);
        Assert.Equal(id, audit.Properties["ConversationId"]);
        Assert.Equal(id.ToString("D"), audit.Properties["ConversationSelector"]);
        Assert.Equal(1, audit.Properties["RowCount"]);
        Assert.Equal(false, audit.Properties["IsError"]);
        Assert.DoesNotContain(id.ToString(), McpToolCallAuditLogger.HashArguments(new { conversationId = id }));
    }

    [Fact]
    public async Task ExplicitListTools_AuditMaterializedPhaseAndTimelineRowCounts()
    {
        var id = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(id);
        metrics.Setup(x => x.GetPhaseBreakdownAsync(
                id,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ConversationPhaseDto { Phase = "early" },
                new ConversationPhaseDto { Phase = "working-memory" }
            ]);
        metrics.Setup(x => x.GetPromptGrowthTimelineAsync(
                id,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptGrowthTimelineDto
            {
                ConversationId = id,
                Points =
                [
                    new PromptGrowthPointDto { TurnIndex = 1 },
                    new PromptGrowthPointDto { TurnIndex = 2 },
                    new PromptGrowthPointDto { TurnIndex = 3 }
                ]
            });
        var logger = new CapturingLogger<McpToolCallAuditLogger>();
        var tools = new ConversationTools(
            metrics.Object,
            new McpToolCallAuditLogger(logger),
            Options.Create(new McpTelemetryOptions()),
            new HttpContextAccessor());

        await tools.GetCompressionPhaseBreakdownAsync(id, CancellationToken.None);
        await tools.GetPromptGrowthTimelineAsync(id, CancellationToken.None);

        var audits = logger.Logs.ToDictionary(
            entry => (string)entry.Properties["ToolName"]!,
            StringComparer.Ordinal);
        Assert.Equal(2, audits["comprexy_get_compression_phase_breakdown"].Properties["RowCount"]);
        Assert.Equal(3, audits["comprexy_get_prompt_growth_timeline"].Properties["RowCount"]);
        Assert.All(audits.Values, audit => Assert.Equal(false, audit.Properties["IsError"]));
    }

    [Fact]
    public async Task ExplicitSummaryToolAndResource_ReturnEquivalentData()
    {
        var id = Guid.NewGuid();
        var metrics = McpTestData.CreateMetrics(id);
        var options = Options.Create(new McpTelemetryOptions());
        var tool = new ConversationTools(
            metrics.Object,
            new McpToolCallAuditLogger(new CapturingLogger<McpToolCallAuditLogger>()),
            options,
            new HttpContextAccessor());
        var resource = new ConversationResources(metrics.Object, options);

        var toolResult = await tool.GetConversationSummaryAsync(id, CancellationToken.None);
        var resourceResult = await resource.GetSummaryAsync(id, CancellationToken.None);

        Assert.Equal(toolResult, resourceResult);
        Assert.Contains(id.ToString(), toolResult);
    }

    [Fact]
    public async Task RetrievalSearchTool_RejectsEmptyQueryAndAuditsSuccess()
    {
        var id = Guid.NewGuid();
        var retrieval = McpTestData.CreateRetrieval(id);
        var logger = new CapturingLogger<McpToolCallAuditLogger>();
        var tools = new ConversationRetrievalTools(
            retrieval.Object,
            new McpToolCallAuditLogger(logger),
            Options.Create(new McpTelemetryOptions()),
            new HttpContextAccessor());

        retrieval.Setup(x => x.SearchAsync(
                id,
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Search query must not be empty."));

        var empty = await tools.SearchConversationAsync(
            id,
            "   ",
            limit: null,
            includeFolded: true,
            includeWorkingMemory: true,
            CancellationToken.None);
        Assert.True(JsonDocument.Parse(empty).RootElement.GetProperty("isError").GetBoolean());

        retrieval.Setup(x => x.SearchAsync(
                id,
                "fingerprint",
                It.IsAny<int?>(),
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSearchResultDto
            {
                ConversationId = id,
                Query = "fingerprint",
                Matches =
                [
                    new ConversationSearchMatchDto
                    {
                        SourceType = "message",
                        Sequence = 2,
                        Text = "hit"
                    }
                ]
            });

        var ok = await tools.SearchConversationAsync(
            id,
            "fingerprint",
            limit: null,
            includeFolded: true,
            includeWorkingMemory: true,
            CancellationToken.None);
        Assert.Contains("\"sequence\":2", ok);
        Assert.Equal(1, logger.Logs.Last().Properties["RowCount"]);
        Assert.Equal(false, logger.Logs.Last().Properties["IsError"]);
    }

    [Fact]
    public async Task CompareTool_AuditsBothSelectorsAndTypedSuccessOrErrorCounts()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var selector = $"{left:D},{right:D}";

        var successMetrics = McpTestData.CreateMetrics(left, right);
        var successLogger = new CapturingLogger<McpToolCallAuditLogger>();
        var successTool = new ConversationTools(
            successMetrics.Object,
            new McpToolCallAuditLogger(successLogger),
            Options.Create(new McpTelemetryOptions()),
            new HttpContextAccessor());

        await successTool.CompareConversationsAsync(left, right, CancellationToken.None);

        var successAudit = Assert.Single(successLogger.Logs);
        Assert.Equal(selector, successAudit.Properties["ConversationSelector"]);
        Assert.Null(successAudit.Properties["ConversationId"]);
        Assert.Equal(2, successAudit.Properties["RowCount"]);
        Assert.Equal(false, successAudit.Properties["IsError"]);

        var errorMetrics = McpTestData.CreateMetrics(left);
        var errorLogger = new CapturingLogger<McpToolCallAuditLogger>();
        var errorTool = new ConversationTools(
            errorMetrics.Object,
            new McpToolCallAuditLogger(errorLogger),
            Options.Create(new McpTelemetryOptions()),
            new HttpContextAccessor());

        var errorPayload = await errorTool.CompareConversationsAsync(
            left,
            right,
            CancellationToken.None);

        Assert.True(JsonDocument.Parse(errorPayload).RootElement.GetProperty("isError").GetBoolean());
        var errorAudit = Assert.Single(errorLogger.Logs);
        Assert.Equal(selector, errorAudit.Properties["ConversationSelector"]);
        Assert.Null(errorAudit.Properties["ConversationId"]);
        Assert.Equal(0, errorAudit.Properties["RowCount"]);
        Assert.Equal(true, errorAudit.Properties["IsError"]);
    }

    [Fact]
    public void McpTelemetryOptions_DefaultsMatchDocumentedContract()
    {
        var options = new McpTelemetryOptions();

        Assert.Equal(100, options.DefaultRowLimit);
        Assert.Equal(1000, options.MaxRowLimit);
        Assert.Equal(5, options.QueryTimeoutSeconds);
    }
}

public sealed class McpAuthenticationTests
{
    [Theory]
    [InlineData("/mcp", null, 401, false)]
    [InlineData("/mcp", "Bearer secret", 200, true)]
    [InlineData("/health", null, 200, true)]
    [InlineData("/v1/comprexy/conversations", null, 401, false)]
    public async Task Middleware_ProtectsMcpAndV1ButLeavesHealthOpen(
        string path,
        string? authorization,
        int expectedStatus,
        bool expectedNext)
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new AuthOptions { RequiredApiKey = "secret" }));
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedNext, nextCalled);
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Entries.Enqueue(message);
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        Logs.Enqueue(new CapturedLog(message, properties));
    }
}

internal sealed record CapturedLog(
    string Message,
    IReadOnlyDictionary<string, object?> Properties);
