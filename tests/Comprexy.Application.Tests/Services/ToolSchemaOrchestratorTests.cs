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

public class ToolSchemaOrchestratorTests
{
    private readonly Mock<IConversationToolCatalogRepository> _catalogRepository = new();
    private readonly Mock<IConversationToolDefinitionRepository> _definitionRepository = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IConversationMetricsRecorder> _metricsRecorder = new();
    private readonly Dictionary<Guid, ConversationToolCatalog> _catalogs = new();
    private readonly List<ConversationToolDefinition> _definitions = [];
    private ToolIrCallIdMap _callIdMap = null!;
    private InMemoryConversationToolCallMapRepository _callIdMapRepo = null!;
    private IToolIrCallIdMapService _callIdMapService = null!;
    private ToolIrFileBodyCache _fileCache = null!;
    private ToolIrResultShapeStore _shapeStore = null!;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    private ToolSchemaOptions _options = new()
    {
        Mode = ToolSchemaMode.Virtual,
        MappingMaxRetries = 2,
        MaxRangeLines = 250,
        CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(30)
    };

    public ToolSchemaOrchestratorTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => _now);
        _catalogRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _catalogs.TryGetValue(id, out var catalog) ? catalog : null);
        // Distinct instance from GetByConversationIdAsync so tests can prove tracking vs detached reads.
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
    }

    private ToolSchemaOrchestrator CreateOrchestrator(
        string? providerModel = "m",
        string? compressionModel = null)
    {
        var toolOptions = Options.Create(_options);
        _fileCache = new ToolIrFileBodyCache(toolOptions);
        _callIdMap = new ToolIrCallIdMap(_clock.Object, toolOptions);
        _callIdMapRepo = new InMemoryConversationToolCallMapRepository();
        _callIdMapService = new ToolIrCallIdMapService(
            _callIdMap,
            new InMemoryToolIrCallIdMapUnitOfWorkFactory(_callIdMapRepo),
            _clock.Object,
            toolOptions);
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://upstream", ApiKey = "k", Model = providerModel }),
            Options.Create(new CompressionOptions { Model = compressionModel }));
        _metricsRecorder.Setup(m => m.IsEnabled).Returns(true);
        _metricsRecorder
            .Setup(m => m.RecordCompressionOverheadAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
            new ToolIrPlanner(toolOptions, _fileCache),
            ToolIrTestFactory.CreateDistiller(toolOptions, _fileCache, _shapeStore = ToolIrTestFactory.CreateShapeStore(_options)),
            _callIdMapService,
            _catalogRepository.Object,
            _definitionRepository.Object,
            _chatCompletionClient.Object,
            _clock.Object,
            _shapeStore,
            NullLogger<ToolSchemaOrchestrator>.Instance);
    }

    private static void AssertOpaqueClientCallId(string clientCallId) =>
        Assert.Matches(@"^cur_[0-9a-f]{32}$", clientCallId);

    private static JsonElement ParseRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadAndShellToolsRequest() =>
        ParseRequest(
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

    private static JsonElement ReadWriteReadLintsToolsRequest() =>
        ParseRequest(
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
                    "name": "Write",
                    "description": "Write a file.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "path": { "type": "string" },
                        "contents": { "type": "string" }
                      },
                      "required": ["path", "contents"]
                    }
                  }
                },
                {
                  "type": "function",
                  "function": {
                    "name": "ReadLints",
                    "description": "Read linter errors.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "paths": {
                          "type": "array",
                          "items": { "type": "string" }
                        }
                      }
                    }
                  }
                }
              ]
            }
            """);

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

    private static string ValidReadShellBoundMappingJson(string schemaHash) =>
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
                    capability = "SHELL_BACKEND",
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
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "direct",
                    arg_map = new
                    {
                        command = "command",
                        working_directory = "working_directory",
                        block_until_ms = "block_until_ms",
                        description = "description"
                    }
                }
            }
        });

    private static string ValidReadWriteReadLintsMappingJson(string schemaHash) =>
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
                    client_tool = "Write",
                    capability = "NON_FILE",
                    risk = "medium",
                    supports = new { path = true, offset = false, limit = false, query = false }
                },
                new
                {
                    client_tool = "ReadLints",
                    capability = "NON_FILE",
                    risk = "low",
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

    private void SetupMapperReturns(string mappingJson, int times = 1)
    {
        var setup = _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()));
        if (times <= 0)
        {
            setup.ThrowsAsync(new InvalidOperationException("mapper should not be called"));
            return;
        }

        setup.ReturnsAsync(new UpstreamChatResult(mappingJson, "stop", 40, 10));
    }

    private void SetupMapperEchoValidMapping()
    {
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                var user = request.Messages.First(m => m.Role == MessageRole.User).Content ?? string.Empty;
                var hash = user.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .First(line => line.StartsWith("schema_hash:", StringComparison.Ordinal))
                    ["schema_hash:".Length..]
                    .Trim();
                return new UpstreamChatResult(ValidReadShellMappingJson(hash), "stop", 1, 1);
            });
    }

    private static ProviderEndpoint ChatEndpoint() => new("http://upstream", "k", "m", 30);

    private static UpstreamRequest ChatUpstream(JsonElement? rewritten, params ChatMessage[] messages) =>
        new(
            messages,
            Stream: false,
            OriginalClientRequest: rewritten,
            ReplaceMessages: true,
            Purpose: UpstreamRequestPurpose.Chat,
            RewrittenClientRequest: rewritten);

    [Fact]
    public async Task TryPrepareRewriteAsync_ModeOff_ReturnsNull()
    {
        _options.Mode = ToolSchemaMode.Off;
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ReadAndShellToolsRequest(),
            CancellationToken.None);

        Assert.Null(outcome.Result);
        Assert.False(outcome.CatalogMutated);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_HashMiss_CallsMapperPersistsMap_SecondRequestSkipsMapper()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var first = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(first.Result);
        Assert.True(first.CatalogMutated);
        Assert.True(_catalogs.TryGetValue(conversationId, out var catalog));
        Assert.False(catalog!.ToolIrDisabled);
        Assert.False(string.IsNullOrWhiteSpace(catalog.MappingJson));
        Assert.Equal(hash, catalog.CatalogHash);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _chatCompletionClient.Reset();
        SetupMapperReturns("should-not-be-used", times: 0);

        var second = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "again")],
            request,
            CancellationToken.None);

        Assert.NotNull(second.Result);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_InvalidPersistedMap_RemapsInsteadOfDisable()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var invalidMap = JsonSerializer.Serialize(new
        {
            schema_hash = hash,
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
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "Shell",
                    strategy = "direct",
                    arg_map = new { path = "command" }
                }
            }
        });
        _catalogs[conversationId] = ConversationToolCatalog.Create(
            conversationId,
            hash,
            invalidMap,
            DateTimeOffset.UtcNow);

        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.True(outcome.CatalogMutated);
        Assert.True(_catalogs.TryGetValue(conversationId, out var catalog));
        Assert.False(catalog!.ToolIrDisabled);
        var expectedPersisted = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<ToolIrMappingDocument>(ValidReadShellMappingJson(hash)));
        Assert.Equal(expectedPersisted, catalog.MappingJson);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_HashMiss_RecordsMapperTokensAsCompressionOverhead()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        _metricsRecorder.Verify(
            m => m.RecordCompressionOverheadAsync(conversationId, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_InvalidMapRetries_RecordsOverheadPerAttempt()
    {
        _options.MappingMaxRetries = 2;
        var request = ReadAndShellToolsRequest();
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("""{"not":"a valid map"}""", "stop", 10, 5));

        await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        _metricsRecorder.Verify(
            m => m.RecordCompressionOverheadAsync(conversationId, 15, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ProviderAndCompressionModelUnset_UsesClientRequestModel()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator(providerModel: null, compressionModel: null);
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.False(_catalogs[conversationId].ToolIrDisabled);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.Is<ProviderEndpoint>(e => e.Model == "client-model"),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_InvalidMapRetries_ThenDisableToolIr_NativeToolsUnchanged()
    {
        _options.MappingMaxRetries = 2;
        var request = ReadAndShellToolsRequest();
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("""{"not":"a valid map"}""", "stop", 1, 1));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.Null(outcome.Result);
        Assert.True(outcome.CatalogMutated);
        Assert.True(_catalogs.TryGetValue(conversationId, out var catalog));
        Assert.True(catalog!.ToolIrDisabled);
        Assert.True(string.IsNullOrWhiteSpace(catalog.MappingJson));
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Second prepare with disabled catalog forwards native tools (null rewrite).
        var again = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.Null(again.Result);
        Assert.False(again.CatalogMutated);
        Assert.True(catalog.ToolIrDisabled);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_OutboundTools_BoundVirtualPlusMetaPlusPassthrough_NoHydrateOrCompactIndex()
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
        var result = outcome.Result!;
        Assert.True(result.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()!)
            .ToList();

        Assert.Contains(ToolSchemaConstants.FileRangeToolName, names);
        Assert.Contains(ToolSchemaConstants.FileManifestToolName, names);
        Assert.Contains(ToolSchemaConstants.ConversationIdMetaToolName, names);
        Assert.Contains("Shell", names);
        Assert.DoesNotContain("Read", names);
        Assert.DoesNotContain("get_tool_definition", names);
        Assert.DoesNotContain(ToolSchemaConstants.FileSearchToolName, names);
        Assert.DoesNotContain(
            result.OutgoingMessages,
            m => m.Role == MessageRole.System &&
                 (m.Content?.Contains("compact", StringComparison.OrdinalIgnoreCase) == true ||
                  m.Content?.Contains("tool schema rules", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_OutboundTools_BoundShell_HidesNativeShell_ShowsComprexyShell()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellBoundMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.True(outcome.Result!.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()!)
            .ToList();

        Assert.Contains(ToolSchemaConstants.ShellToolName, names);
        Assert.Contains(ToolSchemaConstants.FileRangeToolName, names);
        Assert.Contains(ToolSchemaConstants.ConversationIdMetaToolName, names);
        Assert.DoesNotContain("Shell", names);
        Assert.DoesNotContain("Read", names);

        var shellTool = tools.EnumerateArray()
            .First(t => t.GetProperty("function").GetProperty("name").GetString() ==
                        ToolSchemaConstants.ShellToolName);
        var description = shellTool.GetProperty("function").GetProperty("description").GetString()!;
        Assert.DoesNotContain("Git Safety Protocol", description, StringComparison.Ordinal);
        Assert.True(description.Length < 800);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ExcludeFromModelTools_HidesListedTools_KeepsWrite()
    {
        _options.ExcludeFromModelTools = ["ReadLints", "TodoWrite"];
        var request = ReadWriteReadLintsToolsRequest();
        var hash = CatalogHashFor(request);
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadWriteReadLintsMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.Contains("ReadLints", outcome.Result!.Session.ExcludedFromModelToolNames);
        Assert.True(outcome.Result.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()!)
            .ToList();

        Assert.Contains(ToolSchemaConstants.FileRangeToolName, names);
        Assert.Contains("Write", names);
        Assert.DoesNotContain("ReadLints", names);
        Assert.DoesNotContain("Read", names);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ExcludeFromModelTools_IsCaseInsensitive()
    {
        _options.ExcludeFromModelTools = ["readlints"];
        var request = ReadWriteReadLintsToolsRequest();
        var hash = CatalogHashFor(request);
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadWriteReadLintsMappingJson(hash));

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.Contains("ReadLints", outcome.Result!.Session.ExcludedFromModelToolNames);
        Assert.True(outcome.Result.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()!)
            .ToList();

        Assert.DoesNotContain("ReadLints", names);
        Assert.Contains("Write", names);
    }

    [Fact]
    public async Task RunInternalLoopAsync_ExcludedTool_LocalReject_NoClientFacingCall()
    {
        _options.ExcludeFromModelTools = ["ReadLints"];
        var request = ReadWriteReadLintsToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadWriteReadLintsMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        var assistantJson = """
            {"role":"assistant","content":null,"tool_calls":[{"id":"ir_lint_1","type":"function","function":{"name":"ReadLints","arguments":"{\"paths\":[\"apps/dashboard/src/lib/utils.ts\"]}"}}]}
            """;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 1, 1, AssistantMessageJson:
                """{"role":"assistant","content":"done"}"""));

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                RawResponseJson: """{"choices":[{"message":{"role":"assistant","tool_calls":[]},"finish_reason":"tool_calls"}]}""",
                AssistantMessageJson: assistantJson),
            CancellationToken.None);

        Assert.Empty(loop.AllowedRealToolCalls);
        Assert.Equal("done", loop.FinalUpstreamResult.Content);
        Assert.False(_callIdMap.TryGetByIrId(conversationId, "ir_lint_1", out _));
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_ExcludedTool_DropsNativeAssistantAndOrphanResult()
    {
        _options.ExcludeFromModelTools = ["ReadLints"];
        const string clientCallId = "cur_excludedreadlintaaaaaaaaaaaaa";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "ReadLints",
                    "arguments": "{\"paths\":[\"apps/dashboard/src/lib/utils.ts\"]}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "No linter errors found.");
        var hidden = new HashSet<string>(StringComparer.Ordinal) { "ReadLints" };

        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [assistant, tool],
            [],
            [],
            CancellationToken.None,
            hidden);

        Assert.Empty(inbound.Messages);
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ModeOff_IgnoresExcludeFromModelTools()
    {
        _options.Mode = ToolSchemaMode.Off;
        _options.ExcludeFromModelTools = ["ReadLints"];
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ReadWriteReadLintsToolsRequest(),
            CancellationToken.None);

        Assert.Null(outcome.Result);
        Assert.False(outcome.CatalogMutated);
    }

    [Fact]
    public async Task RunInternalLoopAsync_Shell_EmitsNativeShell_AndInboundDistillsTruncatedObservation()
    {
        _options.MaxShellObservationChars = 40;
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellBoundMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_shell_1";
        var assistantJson = IrShellAssistantJson(irCallId, "dotnet build", "/workspace/repo");
        var initial = new UpstreamChatResult(
            string.Empty,
            "tool_calls",
            1,
            1,
            RawResponseJson: """{"choices":[{"message":{"role":"assistant","tool_calls":[]},"finish_reason":"tool_calls"}]}""",
            AssistantMessageJson: assistantJson);

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            initial,
            CancellationToken.None);

        Assert.False(loop.RequiresInternalHandling);
        Assert.Single(loop.AllowedRealToolCalls);
        Assert.Equal("Shell", loop.AllowedRealToolCalls[0].Name);
        using var nativeArgs = JsonDocument.Parse(loop.AllowedRealToolCalls[0].ArgumentsJson);
        Assert.Equal("dotnet build", nativeArgs.RootElement.GetProperty("command").GetString());
        Assert.Equal("/workspace/repo", nativeArgs.RootElement.GetProperty("working_directory").GetString());
        var clientCallId = loop.AllowedRealToolCalls[0].Id;
        AssertOpaqueClientCallId(clientCallId);
        Assert.DoesNotContain("comprexy_shell", loop.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);

        var nativeOutput = new string('x', 80);
        var inbound = await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(clientCallId, nativeOutput)]);

        Assert.Single(inbound);
        Assert.Equal(irCallId, ExtractToolCallId(inbound[0]));
        using var observation = JsonDocument.Parse(inbound[0].Content!);
        Assert.Equal("shell", observation.RootElement.GetProperty("type").GetString());
        Assert.Equal("dotnet build", observation.RootElement.GetProperty("command").GetString());
        Assert.True(observation.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(observation.RootElement.GetProperty("content").GetString()!.Length <= 41);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_ReplacedShellTool_DropsNativeAssistantAndOrphanResult()
    {
        const string clientCallId = "cur_replacedshellaaaaaaaaaaaaaaaaa";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "Shell",
                    "arguments": "{\"command\":\"ls\"}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "total 0");
        var replaced = new HashSet<string>(StringComparer.Ordinal) { "Shell" };

        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [assistant, tool],
            [],
            [],
            CancellationToken.None,
            replaced);

        Assert.Empty(inbound.Messages);
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task RunInternalLoopAsync_FileRangeCacheMiss_EmitsNativeRead_AndInboundDistillsBoundedRange()
    {
        _options.MaxRangeLines = 3;
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_range_1";
        var assistantJson = IrFileRangeAssistantJson(irCallId, "src/A.cs", 1, 10);
        var initial = new UpstreamChatResult(
            string.Empty,
            "tool_calls",
            1,
            1,
            RawResponseJson: """{"choices":[{"message":{"role":"assistant","tool_calls":[]},"finish_reason":"tool_calls"}]}""",
            AssistantMessageJson: assistantJson);

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            initial,
            CancellationToken.None);

        Assert.False(loop.RequiresInternalHandling);
        Assert.Single(loop.AllowedRealToolCalls);
        Assert.Equal("Read", loop.AllowedRealToolCalls[0].Name);
        var clientCallId = loop.AllowedRealToolCalls[0].Id;
        AssertOpaqueClientCallId(clientCallId);
        Assert.True(_callIdMap.TryGetByIrId(conversationId, irCallId, out var mapping));
        Assert.Equal(clientCallId, mapping!.ClientCallId);
        Assert.Contains("Read", loop.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);
        Assert.Contains(clientCallId, loop.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("comprexy_read_file_range", loop.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);

        var nativeBody = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line-{i}"));
        var inbound = await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(clientCallId, nativeBody)]);

        Assert.Single(inbound);
        Assert.Equal(MessageRole.Tool, inbound[0].Role);
        Assert.Equal(irCallId, ExtractToolCallId(inbound[0]));
        using var observation = JsonDocument.Parse(inbound[0].Content!);
        Assert.Equal("file_range", observation.RootElement.GetProperty("type").GetString());
        Assert.True(observation.RootElement.GetProperty("truncated").GetBoolean());
        var content = observation.RootElement.GetProperty("content").GetString()!;
        Assert.Contains("line-1", content, StringComparison.Ordinal);
        Assert.Contains("line-3", content, StringComparison.Ordinal);
        Assert.DoesNotContain("line-4", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunInternalLoopAsync_FileRangeCacheHit_ReturnsLocalObservation_NoNativeRead()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));
        _fileCache.Set(conversationId, "src/A.cs", "alpha\nbeta\ngamma\n", bodyComplete: true);

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_cached";
        var assistantJson = IrFileRangeAssistantJson(irCallId, "src/A.cs", 1, 2);
        var chatRounds = 0;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                chatRounds++;
                return new UpstreamChatResult(
                    "done",
                    "stop",
                    1,
                    1,
                    AssistantMessageJson: """{"role":"assistant","content":"done"}""");
            });

        var initial = new UpstreamChatResult(
            string.Empty,
            "tool_calls",
            1,
            1,
            AssistantMessageJson: assistantJson);

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            initial,
            CancellationToken.None);

        Assert.Empty(loop.AllowedRealToolCalls);
        Assert.Equal("done", loop.FinalUpstreamResult.Content);
        Assert.Equal(1, chatRounds);
        Assert.DoesNotContain(_callIdMap.GetPendingClientIds(conversationId), id => true);
        Assert.Single(prepare.Result!.Session.PendingPersistedTurns);
        var persisted = prepare.Result.Session.PendingPersistedTurns[0];
        Assert.Single(persisted.ToolMessages);
        Assert.Empty(prepare.Result.Session.PendingLocalToolResults);
        Assert.NotNull(persisted.AssistantMessage.RawWireMessage);
        var toolCalls = persisted.AssistantMessage.RawWireMessage!.Value.GetProperty("tool_calls");
        Assert.Equal(irCallId, toolCalls[0].GetProperty("id").GetString());
        Assert.Equal(irCallId, ExtractToolCallId(persisted.ToolMessages[0]));
    }

    [Fact]
    public async Task RunInternalLoopAsync_ParallelIrCalls_EmitParallelNativeIds_AndInboundClosesCorrectIrIds()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irA = "ir_a";
        const string irB = "ir_b";
        var assistantJson = IrParallelFileRangeAssistantJson(irA, "a.cs", irB, "b.cs");

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(string.Empty, "tool_calls", 1, 1, AssistantMessageJson: assistantJson),
            CancellationToken.None);

        Assert.Equal(2, loop.AllowedRealToolCalls.Count);
        Assert.All(loop.AllowedRealToolCalls, c =>
        {
            Assert.Equal("Read", c.Name);
            AssertOpaqueClientCallId(c.Id);
        });
        Assert.True(_callIdMap.TryGetByIrId(conversationId, irA, out var mapA));
        Assert.True(_callIdMap.TryGetByIrId(conversationId, irB, out var mapB));
        Assert.Contains(loop.AllowedRealToolCalls, c => c.Id == mapA!.ClientCallId);
        Assert.Contains(loop.AllowedRealToolCalls, c => c.Id == mapB!.ClientCallId);
        Assert.False(string.Equals(mapA!.ClientCallId, mapB!.ClientCallId, StringComparison.Ordinal));

        var rewritten = await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [
                ToolCallWireHelper.BuildToolResultMessage(mapB!.ClientCallId, "body-b"),
                ToolCallWireHelper.BuildToolResultMessage(mapA!.ClientCallId, "body-a")
            ]);

        Assert.Equal(2, rewritten.Count);
        Assert.Equal(irB, ExtractToolCallId(rewritten[0]));
        Assert.Equal(irA, ExtractToolCallId(rewritten[1]));
        Assert.Contains("body-b", rewritten[0].Content, StringComparison.Ordinal);
        Assert.Contains("body-a", rewritten[1].Content, StringComparison.Ordinal);
        Assert.Empty(_callIdMap.GetPendingClientIds(conversationId));
    }

    [Fact]
    public async Task RunInternalAndStreamingLoops_BothRemapToolCallsTowardClient()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperEchoValidMapping();

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_stream";
        var assistantJson = IrFileRangeAssistantJson(irCallId, "x.cs", 1, 2);
        var initial = new UpstreamChatResult(
            string.Empty,
            "tool_calls",
            1,
            1,
            RawResponseJson: """{"id":"chatcmpl-1","choices":[{"message":{"role":"assistant"},"finish_reason":null}]}""",
            AssistantMessageJson: assistantJson);

        var nonStream = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            initial,
            CancellationToken.None);

        Assert.Contains("Read", nonStream.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);
        var nonStreamClientId = Assert.Single(nonStream.AllowedRealToolCalls).Id;
        AssertOpaqueClientCallId(nonStreamClientId);
        Assert.Contains(nonStreamClientId, nonStream.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("comprexy_read_file_range", nonStream.FinalUpstreamResult.RawResponseJson, StringComparison.Ordinal);

        _callIdMap.ClearConversation(conversationId);
        var streamChunks = new List<string>();
        _chatCompletionClient
            .Setup(c => c.StreamAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (
                    ProviderEndpoint _,
                    UpstreamRequest _,
                    Func<string, CancellationToken, Task> onChunk,
                    CancellationToken token) =>
                {
                    await onChunk(
                        """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"ir_stream2","type":"function","function":{"name":"comprexy_read_file_range","arguments":"{\"path\":\"y.cs\",\"start_line\":1,\"end_line\":1}"}}]},"finish_reason":"tool_calls"}]}""",
                        token);
                    await onChunk("[DONE]", token);
                    return new UpstreamChatResult(
                        string.Empty,
                        "tool_calls",
                        1,
                        1,
                        AssistantMessageJson:
                        """{"role":"assistant","content":null,"tool_calls":[{"id":"ir_stream2","type":"function","function":{"name":"comprexy_read_file_range","arguments":"{\"path\":\"y.cs\",\"start_line\":1,\"end_line\":1}"}}]}""");
                });

        var stream = await orchestrator.RunStreamingLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")) with { Stream = true },
            (chunk, _) =>
            {
                streamChunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var streamCall = Assert.Single(stream.AllowedRealToolCalls, c => c.Name == "Read");
        AssertOpaqueClientCallId(streamCall.Id);
        Assert.True(_callIdMap.TryGetByIrId(conversationId, "ir_stream2", out var streamMapping));
        Assert.Equal(streamCall.Id, streamMapping!.ClientCallId);
        Assert.Contains(streamChunks, c => c.Contains("Read", StringComparison.Ordinal));
        Assert.Contains(streamChunks, c => c.Contains(streamCall.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(streamChunks, c => c.Contains("comprexy_read_file_range", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemappedIrTranscript_AfterInboundDistill_ClosedChainGateAcceptsPersistedMessages()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_closed_chain";
        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                AssistantMessageJson: IrFileRangeAssistantJson(irCallId, "closed.cs", 1, 1)),
            CancellationToken.None);

        var clientCallId = Assert.Single(loop.AllowedRealToolCalls).Id;
        AssertOpaqueClientCallId(clientCallId);

        var inbound = await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(clientCallId, "line-1")]);
        Assert.Single(inbound);

        // Persist/remapped transcript uses IR assistant ids + distilled IR tool results.
        var assistantEntity = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            loop.FinalUpstreamResult.AssistantMessageJson);
        var toolEntity = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            inbound[0].Content ?? string.Empty,
            1,
            DateTimeOffset.UtcNow,
            inbound[0].RawWireMessage?.GetRawText());

        Assert.Contains(irCallId, assistantEntity.RawWireJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(clientCallId, assistantEntity.RawWireJson!, StringComparison.Ordinal);
        Assert.Equal(irCallId, ExtractToolCallId(inbound[0]));
        Assert.False(ToolCallChainState.HasOpenToolCalls([assistantEntity, toolEntity]));
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_AfterMemoryDrop_LoadsMappingFromEf()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_durable";
        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                AssistantMessageJson: IrFileRangeAssistantJson(irCallId, "a.cs", 1, 1)),
            CancellationToken.None);

        var clientCallId = Assert.Single(loop.AllowedRealToolCalls).Id;
        Assert.Single(_callIdMapRepo.Rows);
        _callIdMap.ClearConversation(conversationId);
        Assert.False(_callIdMap.TryGetByClientId(conversationId, clientCallId, out _));

        var inbound = await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(clientCallId, "line-1")]);

        Assert.Single(inbound);
        Assert.Equal(irCallId, ExtractToolCallId(inbound[0]));
        Assert.Empty(_callIdMapRepo.Rows);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_SameBatchAssistantAnnounces_AllowsOrphanedClientId()
    {
        const string clientCallId = "cur_e3d0419465704b7486f1283e9dc46c64";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "read",
                    "arguments": "{\"filePath\":\"docs/b.md\"}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "file body");

        // Without replaced-file set: orphan passthrough (NON_FILE / pre-map) still allowed.
        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [assistant, tool],
            [],
            [],
            CancellationToken.None);

        Assert.Equal(2, inbound.Messages.Count);
        Assert.Equal(MessageRole.Assistant, inbound.Messages[0].Role);
        Assert.Equal(MessageRole.Tool, inbound.Messages[1].Role);
        Assert.Equal(clientCallId, ExtractToolCallId(inbound.Messages[1]));
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_ReplacedFileTools_DropsNativeAssistantAndOrphanResult()
    {
        const string clientCallId = "cur_replacedreadaaaaaaaaaaaaaaaaaa";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "read",
                    "arguments": "{\"filePath\":\"docs/b.md\"}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "file body");
        var replaced = new HashSet<string>(StringComparer.Ordinal) { "read" };

        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [assistant, tool],
            [],
            [],
            CancellationToken.None,
            replaced);

        Assert.Empty(inbound.Messages);
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_ClientSyncedPrefixAnnounces_HealsSnapshotRewindToolTip()
    {
        const string clientCallId = "cur_snapshotrewindaaaaaaaaaaaaaaaa";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "read",
                    "arguments": "{\"filePath\":\"docs/a.md\"}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "result body");

        // Rewind tip: only the tool result is "new"; announcing assistant lives in synced prefix.
        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [tool],
            [],
            [assistant],
            CancellationToken.None);

        Assert.Single(inbound.Messages);
        Assert.Equal(clientCallId, ExtractToolCallId(inbound.Messages[0]));
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_ReplacedFileTools_SwallowsOrphanTipFromSyncedPrefix()
    {
        const string clientCallId = "cur_snapshotreplacedaaaaaaaaaaaaaa";
        var orchestrator = CreateOrchestrator();
        using var assistantDoc = JsonDocument.Parse(
            $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [
                {
                  "id": "{{clientCallId}}",
                  "type": "function",
                  "function": {
                    "name": "read",
                    "arguments": "{\"filePath\":\"docs/a.md\"}"
                  }
                }
              ]
            }
            """);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(clientCallId, "result body");
        var replaced = new HashSet<string>(StringComparer.Ordinal) { "read" };

        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            Guid.NewGuid(),
            [tool],
            [],
            [assistant],
            CancellationToken.None,
            replaced);

        Assert.Empty(inbound.Messages);
        Assert.Empty(inbound.CompletedClientCallIds);
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_SecondResultForCompletedId_IsRejected()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        const string irCallId = "ir_once";
        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                AssistantMessageJson: IrFileRangeAssistantJson(irCallId, "a.cs", 1, 1)),
            CancellationToken.None);

        var clientCallId = Assert.Single(loop.AllowedRealToolCalls).Id;
        await ValidateInboundAndCompleteAsync(
            orchestrator,
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(clientCallId, "line-1")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ValidateAndRewriteInboundToolResultsAsync(
                conversationId,
                [ToolCallWireHelper.BuildToolResultMessage(clientCallId, "again")],
                [],
                [],
                CancellationToken.None));
        Assert.Contains(clientCallId, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnRequestCompletedAsync_FinalAssistantWithoutToolCalls_ClearsPendingRows()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                AssistantMessageJson: IrFileRangeAssistantJson("ir_abandon", "a.cs", 1, 1)),
            CancellationToken.None);

        Assert.NotEmpty(_callIdMapRepo.Rows);
        Assert.NotEmpty(_callIdMap.GetPendingClientIds(conversationId));

        await orchestrator.OnRequestCompletedAsync(
            conversationId,
            """{"role":"assistant","content":"done"}""",
            CancellationToken.None);

        Assert.Empty(_callIdMapRepo.Rows);
        Assert.Empty(_callIdMap.GetPendingClientIds(conversationId));
    }

    [Fact]
    public async Task ValidateAndRewriteInbound_WhenPendingTtlExpired_TreatsAsUnknown()
    {
        _options.CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(5);
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(
                string.Empty,
                "tool_calls",
                1,
                1,
                AssistantMessageJson: IrFileRangeAssistantJson("ir_ttl", "a.cs", 1, 1)),
            CancellationToken.None);

        var clientCallId = Assert.Single(loop.AllowedRealToolCalls).Id;
        _callIdMap.ClearConversation(conversationId);
        _now = _now.AddMinutes(5);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ValidateAndRewriteInboundToolResultsAsync(
                conversationId,
                [ToolCallWireHelper.BuildToolResultMessage(clientCallId, "late")],
                [],
                [],
                CancellationToken.None));
        Assert.Contains(clientCallId, ex.Message, StringComparison.Ordinal);
        Assert.Empty(_callIdMapRepo.Rows);
    }

    [Fact]
    public async Task RunInternalLoopAsync_NativeArgsFailClientSchema_ReturnsLocalError_DoesNotRegister()
    {
        var request = ParseRequest(
            """
            {
              "model": "client-model",
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "glob",
                    "description": "List files.",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "pattern": { "type": "string" },
                        "path": { "type": "string" }
                      },
                      "required": ["pattern", "path"]
                    }
                  }
                }
              ]
            }
            """);
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var mappingJson = JsonSerializer.Serialize(new
        {
            schema_hash = hash,
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "glob",
                    capability = "DIRECTORY_LIST_BACKEND",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = true }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" },
                    // Covers required name for mapper validation, but wrong JSON type for outbound schema.
                    defaults = new { pattern = 123 }
                }
            }
        });
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(mappingJson);

        var prepare = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);

        var assistantJson = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new
                {
                    id = "ir_dir",
                    type = "function",
                    function = new
                    {
                        name = "comprexy_dir_list",
                        arguments = """{"path":"/tmp"}"""
                    }
                }
            }
        });

        var chatRounds = 0;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                chatRounds++;
                return new UpstreamChatResult(
                    "ok",
                    "stop",
                    1,
                    1,
                    AssistantMessageJson: """{"role":"assistant","content":"ok"}""");
            });

        var loop = await orchestrator.RunInternalLoopAsync(
            prepare.Result!.Session,
            ChatEndpoint(),
            ChatUpstream(prepare.Result!.RewrittenClientRequest, new ChatMessage(MessageRole.User, "hello")),
            new UpstreamChatResult(string.Empty, "tool_calls", 1, 1, AssistantMessageJson: assistantJson),
            CancellationToken.None);

        Assert.Empty(loop.AllowedRealToolCalls);
        Assert.Equal(1, chatRounds);
        Assert.Empty(_callIdMapRepo.Rows);
        Assert.Empty(_callIdMap.GetPendingClientIds(conversationId));
        Assert.Equal("ok", loop.FinalUpstreamResult.Content);
    }

    [Fact]
    public async Task ValidateInbound_SuccessfulEdit_InvalidatesFileCache_ForcesNextRangeNative()
    {
        var orchestrator = CreateOrchestrator();
        var conversationId = Guid.NewGuid();
        _fileCache.Set(conversationId, "docs/a.md", "stale body line-1\nstale body line-2", bodyComplete: true);
        Assert.True(_fileCache.TryGet(conversationId, "docs/a.md", out _));

        const string editCallId = "call_edit_1";
        var assistantWire = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new
                {
                    id = editCallId,
                    type = "function",
                    function = new
                    {
                        name = "edit",
                        arguments = JsonSerializer.Serialize(new
                        {
                            filePath = "docs/a.md",
                            oldString = "stale",
                            newString = "fresh"
                        })
                    }
                }
            }
        });
        using var assistantDoc = JsonDocument.Parse(assistantWire);
        var assistant = new ChatMessage(
            MessageRole.Assistant,
            string.Empty,
            assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(editCallId, "Edit applied successfully.");

        await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId,
            [assistant, tool],
            [],
            [],
            CancellationToken.None);

        Assert.False(_fileCache.TryGet(conversationId, "docs/a.md", out _));
    }

    private async Task<IReadOnlyList<ChatMessage>> ValidateInboundAndCompleteAsync(
        ToolSchemaOrchestrator orchestrator,
        Guid conversationId,
        IReadOnlyList<ChatMessage> newClientMessages,
        IReadOnlyList<ConversationMessage>? history = null,
        IReadOnlyList<ChatMessage>? clientSyncedPrefix = null)
    {
        var result = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId,
            newClientMessages,
            history ?? [],
            clientSyncedPrefix ?? [],
            CancellationToken.None);
        foreach (var clientCallId in result.CompletedClientCallIds)
        {
            await orchestrator.CompleteInboundToolCallAsync(conversationId, clientCallId, CancellationToken.None);
        }

        return result.Messages;
    }

    private static string IrFileRangeAssistantJson(string callId, string path, int startLine, int endLine)
    {
        var args = JsonSerializer.Serialize(new { path, start_line = startLine, end_line = endLine });
        return JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new
                {
                    id = callId,
                    type = "function",
                    function = new { name = "comprexy_read_file_range", arguments = args }
                }
            }
        });
    }

    private static string IrShellAssistantJson(string callId, string command, string? workingDirectory = null)
    {
        object argsObj = workingDirectory is null
            ? new { command }
            : new { command, working_directory = workingDirectory };
        var args = JsonSerializer.Serialize(argsObj);
        return JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new
                {
                    id = callId,
                    type = "function",
                    function = new { name = "comprexy_shell", arguments = args }
                }
            }
        });
    }

    private static string IrParallelFileRangeAssistantJson(string callA, string pathA, string callB, string pathB)
    {
        static object Call(string id, string path) => new
        {
            id,
            type = "function",
            function = new
            {
                name = "comprexy_read_file_range",
                arguments = JsonSerializer.Serialize(new { path, start_line = 1, end_line = 1 })
            }
        };

        return JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[] { Call(callA, pathA), Call(callB, pathB) }
        });
    }

    private static string? ExtractToolCallId(ChatMessage message)
    {
        if (message.RawWireMessage is null)
        {
            return null;
        }

        var wire = message.RawWireMessage.Value;
        if (wire.TryGetProperty("tool_call_id", out var id))
        {
            return id.GetString();
        }

        return null;
    }

    [Fact]
    public async Task BuildRewrittenClientRequest_CapDisclosure_ParametersByteIdentical()
    {
        _options.FirstReadMaxLines = 123;
        _options.MaxRangeLines = 77;
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));
        var prepare = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            request,
            CancellationToken.None);
        Assert.NotNull(prepare.Result);
        using var rewritten = JsonDocument.Parse(prepare.Result!.RewrittenClientRequest.GetRawText());
        var tools = rewritten.RootElement.GetProperty("tools");
        JsonElement? range = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("function").GetProperty("name").GetString() == "comprexy_read_file_range")
            {
                range = tool;
                break;
            }
        }

        Assert.NotNull(range);
        var description = range!.Value.GetProperty("function").GetProperty("description").GetString()!;
        Assert.Contains("123", description, StringComparison.Ordinal);
        Assert.Contains("77", description, StringComparison.Ordinal);

        using var built = JsonDocument.Parse(
            ToolIrVirtualToolDefinitions.BuildWireJson(ToolSchemaConstants.FileRangeToolName, _options));
        using var canonical = JsonDocument.Parse(
            ToolIrVirtualToolDefinitions.GetWireJson(ToolSchemaConstants.FileRangeToolName));
        Assert.Equal(
            canonical.RootElement.GetProperty("function").GetProperty("parameters").GetRawText(),
            built.RootElement.GetProperty("function").GetProperty("parameters").GetRawText());

        // Rewritten request re-serializes the tool catalog; parameters must stay semantically identical.
        using var emittedParams = JsonDocument.Parse(
            range.Value.GetProperty("function").GetProperty("parameters").GetRawText());
        Assert.True(
            JsonElement.DeepEquals(
                emittedParams.RootElement,
                canonical.RootElement.GetProperty("function").GetProperty("parameters")));
    }

    [Fact]
    public void Hydrate_LeavesDurableClean_AndDoesNotClobberDirty()
    {
        var orchestrator = CreateOrchestrator();
        var conversationId = Guid.NewGuid();
        var durable = new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.Plain,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Probe,
            ObservedAt = DateTimeOffset.UtcNow
        };
        _shapeStore.Hydrate(conversationId, new Dictionary<string, ToolIrResultShape> { ["Read"] = durable });
        Assert.Empty(_shapeStore.PeekDirty(conversationId));

        _shapeStore.Promote((conversationId, "Grep"), new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.JsonField,
            JsonField = ToolIrJsonFieldToken.Content,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Learner,
            ObservedAt = DateTimeOffset.UtcNow
        });
        Assert.NotEmpty(_shapeStore.PeekDirty(conversationId));
        _shapeStore.Hydrate(conversationId, new Dictionary<string, ToolIrResultShape>());
        Assert.True(_shapeStore.PeekDirty(conversationId).ContainsKey("Grep"));
        _ = orchestrator;
    }

    [Fact]
    public async Task ValidateInbound_StagedShapes_UsesTrackedRead_DirtySurvivesWithoutConfirm()
    {
        var request = ReadAndShellToolsRequest();
        var hash = CatalogHashFor(request);
        var conversationId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator();
        SetupMapperReturns(ValidReadShellMappingJson(hash));
        _catalogs[conversationId] = ConversationToolCatalog.Create(
            conversationId, hash, ValidReadShellMappingJson(hash), _now);

        await _callIdMapService.RegisterAsync(
            new ToolIrCallMapping(
                conversationId,
                "ir_shape",
                "cur_cccccccccccccccccccccccccccccccc",
                ToolSchemaConstants.FileRangeToolName,
                "Read",
                """{"path":"docs/a.md","start_line":1,"end_line":1}""",
                """{"path":"docs/a.md"}""",
                "direct",
                "docs/a.md",
                1,
                1,
                Pending: true),
            CancellationToken.None);

        var first = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(
                "cur_cccccccccccccccccccccccccccccccc",
                "<path>docs/a.md</path><type>file</type><content>\nhi\n</content>")],
            [],
            [],
            CancellationToken.None);
        Assert.Contains("Read", first.StagedShapeClientToolNames);
        _catalogRepository.Verify(
            r => r.GetTrackedByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // No confirm → dirty survives
        Assert.NotEmpty(_shapeStore.PeekDirty(conversationId));
        var second = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId,
            [],
            [],
            [],
            CancellationToken.None);
        Assert.Contains("Read", second.StagedShapeClientToolNames);

        orchestrator.ConfirmShapeMirrorPersisted(conversationId, second.StagedShapeClientToolNames);
        var third = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId, [], [], [], CancellationToken.None);
        Assert.Empty(third.StagedShapeClientToolNames);
    }
}
