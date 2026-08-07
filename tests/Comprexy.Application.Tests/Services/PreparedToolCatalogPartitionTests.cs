using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class PreparedToolCatalogPartitionTests
{
    private readonly Mock<IConversationToolCatalogRepository> _catalogRepository = new();
    private readonly Mock<IConversationToolDefinitionRepository> _definitionRepository = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IConversationMetricsRecorder> _metricsRecorder = new();
    private readonly Dictionary<Guid, ConversationToolCatalog> _catalogs = new();
    private readonly List<ConversationToolDefinition> _definitions = [];
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    private ToolSchemaOptions _options = new()
    {
        Mode = ToolSchemaMode.Virtual,
        MappingMaxRetries = 2,
        MaxRangeLines = 250,
        CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(30)
    };

    public PreparedToolCatalogPartitionTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => _now);
        _catalogRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _catalogs.TryGetValue(id, out var catalog) ? catalog : null);
        _catalogRepository
            .Setup(r => r.GetTrackedByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                if (!_catalogs.TryGetValue(id, out var catalog))
                {
                    return null;
                }

                var tracked = ConversationToolCatalog.Create(
                    catalog.ConversationId,
                    catalog.CatalogHash,
                    catalog.MappingJson,
                    _now,
                    catalog.ToolIrDisabled);
                _catalogs[id] = tracked;
                return tracked;
            });
        _catalogRepository
            .Setup(r => r.Add(It.IsAny<ConversationToolCatalog>()))
            .Callback<ConversationToolCatalog>(c => _catalogs[c.ConversationId] = c);
        _definitionRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _definitions.ToList());
        _definitionRepository
            .Setup(r => r.Add(It.IsAny<ConversationToolDefinition>()))
            .Callback<ConversationToolDefinition>(d => _definitions.Add(d));
        _metricsRecorder.Setup(m => m.IsEnabled).Returns(true);
        _metricsRecorder
            .Setup(m => m.RecordCompressionOverheadAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task BuildModelFacingToolDefinitions_MatchesRewrittenClientRequestToolOrder()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        var session = outcome.Result!.Session;
        var (virtualAndMeta, client) = PreparedToolCatalogPartition.BuildModelFacingToolDefinitions(
            session,
            orchestrator.EffectiveOptions);

        var partitionNames = virtualAndMeta
            .Concat(client)
            .Select(WireFunctionName)
            .ToList();

        Assert.True(outcome.Result.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        var rewrittenNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()!)
            .ToList();

        Assert.Equal(rewrittenNames, partitionNames);
        Assert.Equal(
            ToolSchemaConstants.ConversationIdMetaToolName,
            WireFunctionName(virtualAndMeta[^1]));
        Assert.DoesNotContain("Read", rewrittenNames);
        Assert.Contains("Shell", rewrittenNames);
    }

    [Fact]
    public void BuildModelFacingToolDefinitions_SkipsHiddenAndReserved_OrdersOrdinal()
    {
        var options = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        var session = new ToolSchemaSession
        {
            ConversationId = Guid.NewGuid(),
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "zeta", "alpha", "Read" },
            Mapping = new ToolIrMappingDocument(),
            FullDefinitionsByName =
            {
                ["zeta"] = """{"type":"function","function":{"name":"zeta","parameters":{"type":"object"}}}""",
                ["alpha"] = """{"type":"function","function":{"name":"alpha","parameters":{"type":"object"}}}""",
                ["Read"] = """{"type":"function","function":{"name":"Read","parameters":{"type":"object"}}}""",
                [ToolSchemaConstants.ConversationIdMetaToolName] =
                    ToolSchemaConstants.ConversationIdMetaToolWireJson
            },
            BoundVirtualToolNames =
            {
                ToolSchemaConstants.FileSearchToolName,
                ToolSchemaConstants.FileRangeToolName
            },
            ReplacedClientToolNames = { "Read" }
        };

        var (virtualAndMeta, client) = PreparedToolCatalogPartition.BuildModelFacingToolDefinitions(
            session,
            options);

        Assert.Equal(
            [
                ToolSchemaConstants.FileRangeToolName,
                ToolSchemaConstants.FileSearchToolName,
                ToolSchemaConstants.ConversationIdMetaToolName
            ],
            virtualAndMeta.Select(WireFunctionName).ToList());
        Assert.Equal(["alpha", "zeta"], client.Select(WireFunctionName).ToList());
    }

    private ToolSchemaOrchestrator CreateOrchestrator()
    {
        var toolOptions = Options.Create(_options);
        var fileCache = new ToolIrFileBodyCache(toolOptions);
        var callIdMap = new ToolIrCallIdMap(_clock.Object, toolOptions);
        var callIdMapService = new ToolIrCallIdMapService(
            callIdMap,
            new InMemoryToolIrCallIdMapUnitOfWorkFactory(new InMemoryConversationToolCallMapRepository()),
            _clock.Object,
            toolOptions);
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://upstream.example.test", ApiKey = "k", Model = "m" }),
            Options.Create(new CompressionOptions()));
        var shapeStore = ToolIrTestFactory.CreateShapeStore(_options);

        return new ToolSchemaOrchestrator(
            toolOptions,
            new ToolCatalogParser(),
            new ToolArgumentValidator(),
            new ToolIrSchemaMapper(
                toolOptions,
                Options.Create(new CompressionOptions()),
                endpointResolver,
                _chatCompletionClient.Object,
                Mock.Of<ITokenEstimator>(),
                _metricsRecorder.Object,
                NullLogger<ToolIrSchemaMapper>.Instance),
            new ToolIrPlanner(toolOptions, fileCache),
            ToolIrTestFactory.CreateDistiller(toolOptions, fileCache, shapeStore),
            callIdMapService,
            _catalogRepository.Object,
            _definitionRepository.Object,
            _chatCompletionClient.Object,
            _clock.Object,
            shapeStore,
            NullLogger<ToolSchemaOrchestrator>.Instance);
    }

    private void SetupMapperReturns(string mappingJson)
    {
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(mappingJson, "stop", 40, 10));
    }

    private static string WireFunctionName(string wireJson)
    {
        using var doc = JsonDocument.Parse(wireJson);
        return doc.RootElement.GetProperty("function").GetProperty("name").GetString()!;
    }

    private static JsonElement ReadAndShellToolsRequest()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "model": "client-model",
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "Read",
                    "description": "Read a file.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "path": { "type": "string" }
                      },
                      "required": ["path"]
                    }
                  }
                },
                {
                  "type": "function",
                  "function": {
                    "name": "Shell",
                    "description": "Run a shell command.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "command": { "type": "string" }
                      },
                      "required": ["command"]
                    }
                  }
                }
              ]
            }
            """);
        return document.RootElement.Clone();
    }

    private static string CatalogHashFor(JsonElement request)
    {
        var parsed = new ToolCatalogParser().TryParse(request);
        Assert.NotNull(parsed);
        return parsed!.CatalogHash;
    }

    private static string ValidReadShellMappingJson(string schemaHash) =>
        JsonSerializer.Serialize(new
        {
            schema_hash = schemaHash,
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Read",
                    capability = "FILE_READ_RAW",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = false }
                },
                new
                {
                    client_tool = "Shell",
                    capability = "NON_FILE",
                    risk = "high",
                    supports = new { path = false, offset = false, limit = false, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "Read",
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "Read",
                    strategy = "direct",
                    arg_map = new { path = "path" }
                }
            }
        });
}
