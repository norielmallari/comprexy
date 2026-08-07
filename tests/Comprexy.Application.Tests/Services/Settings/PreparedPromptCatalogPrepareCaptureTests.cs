using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

public class PreparedPromptCatalogPrepareCaptureTests
{
    [Fact]
    public async Task Full_VirtualToolsRewrite_CapturesVirtualIncludingMeta_AndClientPassthrough()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual, MappingMaxRetries = 2 },
            Metrics = new() { Enabled = true },
            EstimatedTokens = 100
        };
        SetupWireToolsEstimate(h, clientToolsTokens: 17);
        var request = BuildReadShellRequest("prep-vt-full");
        var hash = CatalogHashFor(request.RawRequest);
        SetupMapperReturns(h, ValidReadShellMappingJson(hash));

        var prepared = await h.CreatePreparer().PrepareAsync(
            request,
            "header:prep-vt-full",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.NotNull(prepared.MetricsPrepare);
        var metrics = prepared.MetricsPrepare!;
        // CountTokens mock returns 5 per wire string; range + manifest + meta = 15.
        Assert.Equal(15, metrics.PreparedVirtualToolSchemaTokensEstimated);
        Assert.True(metrics.PreparedVirtualToolSchemaTokensEstimated > 0);
        // Shell remains client passthrough under NON_FILE mapping.
        Assert.Equal(5, metrics.PreparedClientToolSchemaTokensEstimated);
        Assert.Equal(0, metrics.PreparedRulesTokensEstimated);
        h.TokenEstimator.Verify(
            t => t.CountPromptSideToolsTokens(It.IsAny<JsonElement?>()),
            Times.Never);
    }

    [Fact]
    public async Task Full_ToolSchemaOff_VirtualZero_ClientFromWireTools()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Off },
            Metrics = new() { Enabled = true },
            EstimatedTokens = 100
        };
        SetupWireToolsEstimate(h, clientToolsTokens: 42);

        var prepared = await h.CreatePreparer().PrepareAsync(
            SliceCTestHarness.BuildRequest("prep-vt-off"),
            "header:prep-vt-off",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.NotNull(prepared.MetricsPrepare);
        Assert.Equal(0, prepared.MetricsPrepare!.PreparedVirtualToolSchemaTokensEstimated);
        Assert.Equal(42, prepared.MetricsPrepare.PreparedClientToolSchemaTokensEstimated);
        Assert.Equal(0, prepared.MetricsPrepare.PreparedRulesTokensEstimated);
        h.TokenEstimator.Verify(
            t => t.CountPromptSideToolsTokens(It.IsAny<JsonElement?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Full_DisableToolIr_VirtualZero_ClientFromWireTools()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual, MappingMaxRetries = 0 },
            Metrics = new() { Enabled = true },
            EstimatedTokens = 100
        };
        SetupWireToolsEstimate(h, clientToolsTokens: 33);
        var request = BuildReadShellRequest("prep-disable-ir");
        var hash = CatalogHashFor(request.RawRequest);
        var conversation = Conversation.Create("header:prep-disable-ir", DateTimeOffset.UtcNow);
        h.SeedExistingConversation(conversation);
        h.ToolCatalogRepository
            .Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationToolCatalog.Create(
                conversation.Id,
                hash,
                mappingJson: string.Empty,
                snapshottedAt: DateTimeOffset.UtcNow,
                toolIrDisabled: true));
        h.ToolCatalogRepository
            .Setup(r => r.GetTrackedByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationToolCatalog.Create(
                conversation.Id,
                hash,
                mappingJson: string.Empty,
                snapshottedAt: DateTimeOffset.UtcNow,
                toolIrDisabled: true));

        var prepared = await h.CreatePreparer().PrepareAsync(
            request,
            "header:prep-disable-ir",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.NotNull(prepared.MetricsPrepare);
        Assert.Equal(0, prepared.MetricsPrepare!.PreparedVirtualToolSchemaTokensEstimated);
        Assert.Equal(33, prepared.MetricsPrepare.PreparedClientToolSchemaTokensEstimated);
        h.TokenEstimator.Verify(
            t => t.CountPromptSideToolsTokens(It.IsAny<JsonElement?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task MonitorOnly_MetricsOn_ClientFromWire_VirtualAndRulesZero()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual },
            Metrics = new() { Enabled = true }
        };
        SetupWireToolsEstimate(h, clientToolsTokens: 19);

        var prepared = await h.CreatePreparer().PrepareAsync(
            SliceCTestHarness.BuildRequest("prep-mon"),
            "header:prep-mon",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.NotNull(prepared.MetricsPrepare);
        Assert.Equal(0, prepared.MetricsPrepare!.PreparedVirtualToolSchemaTokensEstimated);
        Assert.Equal(19, prepared.MetricsPrepare.PreparedClientToolSchemaTokensEstimated);
        Assert.Equal(0, prepared.MetricsPrepare.PreparedRulesTokensEstimated);
        h.TokenEstimator.Verify(
            t => t.CountPromptSideToolsTokens(It.IsAny<JsonElement?>()),
            Times.Once);
    }

    [Fact]
    public async Task PassThrough_MetricsPrepareRemainsNull()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { PassThrough = true, OptimizationMode = OptimizationMode.Full },
            Metrics = new() { Enabled = true },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual }
        };

        var prepared = await h.CreatePreparer().PrepareAsync(
            SliceCTestHarness.BuildRequest("prep-pt"),
            "header:prep-pt",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(prepared.MetricsPrepare);
    }

    private static void SetupWireToolsEstimate(SliceCTestHarness h, int clientToolsTokens)
    {
        h.TokenEstimator
            .Setup(t => t.CountPromptSideToolsTokens(It.IsAny<JsonElement?>()))
            .Returns(clientToolsTokens);
    }

    private static void SetupMapperReturns(SliceCTestHarness h, string mappingJson)
    {
        h.ChatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(mappingJson, "stop", 40, 10));
        h.ChatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));
    }

    private static IncomingChatRequest BuildReadShellRequest(string conversationHeader)
    {
        using var document = JsonDocument.Parse(
            """
            {
              "model": "client-model",
              "stream": false,
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
              ],
              "messages": [
                { "role": "system", "content": "You are helpful." },
                { "role": "user", "content": "Hello" }
              ]
            }
            """);
        return Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            conversationHeader);
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
