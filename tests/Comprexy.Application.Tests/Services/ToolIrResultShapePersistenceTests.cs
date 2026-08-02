using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ToolIrResultShapePersistenceTests
{
    [Fact]
    public async Task GetByConversationId_Detached_ReplaceMapping_DoesNotPersist_TrackedDoes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ComprexyDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ClusterIdSaveChangesInterceptor())
            .Options;

        var conversationId = Guid.NewGuid();
        const string originalJson = """{"schema_hash":"h","client_capabilities":[],"bindings":[]}""";
        const string updatedJson = """{"schema_hash":"h","client_capabilities":[],"bindings":[],"result_shapes":{}}""";

        await using (var bootstrap = new ComprexyDbContext(options))
        {
            await bootstrap.Database.EnsureCreatedAsync();
            bootstrap.ConversationToolCatalogs.Add(ConversationToolCatalog.Create(
                conversationId, "h", originalJson, DateTimeOffset.UnixEpoch));
            await bootstrap.SaveChangesAsync();
        }

        await using (var ctx = new ComprexyDbContext(options))
        {
            var repo = new EfConversationToolCatalogRepository(ctx);
            var detached = await repo.GetByConversationIdAsync(conversationId, CancellationToken.None);
            Assert.NotNull(detached);
            detached!.ReplaceMapping("h", updatedJson, DateTimeOffset.UnixEpoch);
            await ctx.SaveChangesAsync();
        }

        await using (var verify = new ComprexyDbContext(options))
        {
            var row = await verify.ConversationToolCatalogs.AsNoTracking()
                .FirstAsync(c => c.ConversationId == conversationId);
            Assert.Equal(originalJson, row.MappingJson);
        }

        await using (var ctx = new ComprexyDbContext(options))
        {
            var repo = new EfConversationToolCatalogRepository(ctx);
            var tracked = await repo.GetTrackedByConversationIdAsync(conversationId, CancellationToken.None);
            Assert.NotNull(tracked);
            tracked!.ReplaceMapping("h", updatedJson, DateTimeOffset.UnixEpoch);
            await ctx.SaveChangesAsync();
        }

        await using (var verify = new ComprexyDbContext(options))
        {
            var row = await verify.ConversationToolCatalogs.AsNoTracking()
                .FirstAsync(c => c.ConversationId == conversationId);
            Assert.Equal(updatedJson, row.MappingJson);
        }
    }

    [Fact]
    public async Task InboundMirror_Flush_PersistsResultShapes_AndPrepareSeesThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ComprexyDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ClusterIdSaveChangesInterceptor())
            .Options;

        await using var ctx = new ComprexyDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var conversationId = Guid.NewGuid();
        var mappingJson = """
            {
              "schema_hash":"hash1",
              "client_capabilities":[{"client_tool":"Read","capability":"FILE_READ_RAW","risk":"low","supports":{"path":true,"offset":true,"limit":true,"query":false}}],
              "bindings":[{"comprexy_tool":"comprexy_read_file_range","primary_client_tool":"Read","strategy":"direct","arg_map":{"path":"path","start_line":"offset","end_line":"limit"}}]
            }
            """;
        ctx.ConversationToolCatalogs.Add(ConversationToolCatalog.Create(
            conversationId, "hash1", mappingJson, DateTimeOffset.UnixEpoch));
        await ctx.SaveChangesAsync();

        var toolOptions = Options.Create(new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual });
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        var shapeStore = new ToolIrResultShapeStore(toolOptions);
        var fileCache = new ToolIrFileBodyCache(toolOptions);
        var callIdMap = new ToolIrCallIdMap(clock.Object, toolOptions);
        var callIdRepo = new InMemoryConversationToolCallMapRepository();
        var callIdService = new ToolIrCallIdMapService(
            callIdMap,
            new InMemoryToolIrCallIdMapUnitOfWorkFactory(callIdRepo),
            clock.Object,
            toolOptions);
        var catalogRepo = new EfConversationToolCatalogRepository(ctx);
        var defRepo = new Mock<IConversationToolDefinitionRepository>();
        defRepo.Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var chat = new Mock<IChatCompletionClient>();
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://example.test", ApiKey = "k", Model = "m" }),
            Options.Create(new CompressionOptions()));

        var orchestrator = new ToolSchemaOrchestrator(
            toolOptions,
            new ToolCatalogParser(),
            new ToolArgumentValidator(),
            new ToolIrSchemaMapper(
                toolOptions,
                Options.Create(new CompressionOptions()),
                endpointResolver,
                chat.Object,
                Mock.Of<ITokenEstimator>(),
                Mock.Of<IConversationMetricsRecorder>(m => m.IsEnabled == false),
                NullLogger<ToolIrSchemaMapper>.Instance),
            new ToolIrPlanner(toolOptions, fileCache),
            ToolIrTestFactory.CreateDistiller(toolOptions, fileCache, shapeStore),
            callIdService,
            catalogRepo,
            defRepo.Object,
            chat.Object,
            clock.Object,
            shapeStore,
            NullLogger<ToolSchemaOrchestrator>.Instance);

        await callIdService.RegisterAsync(
            new ToolIrCallMapping(
                conversationId,
                "ir_1",
                "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
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

        var inbound = await orchestrator.ValidateAndRewriteInboundToolResultsAsync(
            conversationId,
            [ToolCallWireHelper.BuildToolResultMessage(
                "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "<path>docs/a.md</path><type>file</type><content>\nhello\n</content>")],
            [],
            [],
            CancellationToken.None);

        Assert.Contains("Read", inbound.StagedShapeClientToolNames);
        await new EfUnitOfWork(ctx).SaveChangesAsync(CancellationToken.None);
        orchestrator.ConfirmShapeMirrorPersisted(conversationId, inbound.StagedShapeClientToolNames);
        Assert.Empty(shapeStore.PeekDirty(conversationId));

        await using (var verify = new ComprexyDbContext(options))
        {
            var row = await verify.ConversationToolCatalogs.AsNoTracking()
                .FirstAsync(c => c.ConversationId == conversationId);
            using var doc = JsonDocument.Parse(row.MappingJson);
            Assert.True(doc.RootElement.TryGetProperty("result_shapes", out var shapes));
            Assert.True(shapes.TryGetProperty("Read", out var readShape));
            Assert.Equal("tagged_content", readShape.GetProperty("envelope").GetString());
        }

        var tracked = await catalogRepo.GetTrackedByConversationIdAsync(conversationId, CancellationToken.None);
        Assert.NotNull(tracked);
        Assert.Contains("result_shapes", tracked!.MappingJson, StringComparison.Ordinal);
    }
}
