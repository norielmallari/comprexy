using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class ToolIrFileBodyCacheAndDistillTests
{
    [Fact]
    public void ExtractFileBody_StripsCursorReadWrapperAndLinePrefixes()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var distiller = new ToolIrResultDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = new ToolIrCallMapping(
            conversationId,
            "ir_1",
            "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ToolSchemaConstants.FileRangeToolName,
            "Read",
            """{"path":"docs/a.md","start_line":1,"end_line":2}""",
            """{"filePath":"docs/a.md"}""",
            "read_then_slice",
            "docs/a.md",
            1,
            2,
            Pending: true);

        var native = """
            <path>/workspace/repo/docs/a.md</path>
            <type>file</type>
            <content>
            1: # Title
            2: Hello
            3: World
            </content>
            """;

        var observationJson = distiller.Distill(conversationId, mapping, native);
        using var observation = System.Text.Json.JsonDocument.Parse(observationJson);
        var content = observation.RootElement.GetProperty("content").GetString()!;

        Assert.DoesNotContain("<path>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<content>", content, StringComparison.Ordinal);
        Assert.Equal("# Title\nHello", content);
        Assert.True(cache.TryGet(conversationId, "docs/a.md", out var cached));
        Assert.Equal("# Title\nHello\nWorld\n", cached!.Body);
    }

    [Fact]
    public void DistillFileRange_PartialWindow_DoesNotPoisonCache_AndReturnsContent()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 200 });
        var cache = new ToolIrFileBodyCache(options);
        var distiller = new ToolIrResultDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = new ToolIrCallMapping(
            conversationId,
            "ir_1",
            "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ToolSchemaConstants.FileRangeToolName,
            "read",
            """{"path":"docs/a.md","start_line":175,"end_line":200}""",
            """{"filePath":"docs/a.md","offset":175,"limit":26}""",
            "direct",
            "docs/a.md",
            175,
            200,
            Pending: true);

        var native = """
            <path>/workspace/repo/docs/a.md</path>
            <type>file</type>
            <content>
            175: ---
            176: 
            177: ## Theme Support
            178: body
            </content>
            """;

        var observationJson = distiller.Distill(conversationId, mapping, native);
        using var observation = System.Text.Json.JsonDocument.Parse(observationJson);

        Assert.Equal("file_range", observation.RootElement.GetProperty("type").GetString());
        Assert.False(observation.RootElement.GetProperty("truncated").GetBoolean());
        var content = observation.RootElement.GetProperty("content").GetString()!;
        Assert.Contains("## Theme Support", content, StringComparison.Ordinal);
        Assert.Contains("---", content, StringComparison.Ordinal);
        Assert.False(cache.TryGet(conversationId, "docs/a.md", out _));
        Assert.False(cache.TryGetCovering(conversationId, "docs/a.md", 175, 200, out _));
    }

    [Fact]
    public void SetIfRicher_DoesNotReplaceLongerBodyWithShorterWindow()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var conversationId = Guid.NewGuid();
        cache.Set(conversationId, "docs/a.md", "l1\nl2\nl3\nl4\nl5\n");

        var kept = cache.SetIfRicher(conversationId, "docs/a.md", "partial\n");

        Assert.Equal(5, ToolIrFileBodyCache.ContentLineCount(kept));
        Assert.True(cache.TryGet(conversationId, "docs/a.md", out var cached));
        Assert.Equal("l1\nl2\nl3\nl4\nl5\n", cached!.Body);
    }

    [Fact]
    public void TrySliceLines_PastCachedEnd_ReturnsFalse()
    {
        var entry = ToolIrFileBodyCache.BuildEntry("docs/a.md", "a\nb\nc\n");

        Assert.False(ToolIrFileBodyCache.TrySliceLines(entry, 175, 200, 50, out var text, out _));
        Assert.Equal(string.Empty, text);
        Assert.True(ToolIrFileBodyCache.TrySliceLines(entry, 2, 3, 50, out text, out var truncated));
        Assert.Equal("b\nc", text);
        Assert.False(truncated);
    }

    [Fact]
    public void TryGetCovering_ShortCache_MissesHighRange()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var conversationId = Guid.NewGuid();
        // Simulate the old poison: stripped window stored as lines 1..n
        cache.Set(conversationId, "docs/a.md", "---\n\n## Theme Support\nbody\n");

        Assert.True(cache.TryGetCovering(conversationId, "docs/a.md", 1, 2, out _));
        Assert.False(cache.TryGetCovering(conversationId, "docs/a.md", 175, 200, out _));
    }

    [Fact]
    public void TryGetCovering_TruncatedPrefixCache_MissesRangePastCachedEnd()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var conversationId = Guid.NewGuid();
        // Native Read returned only lines 1-80 of a longer file (pagination truncated).
        var body = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"line-{i}")) + "\n";
        cache.Set(conversationId, "comprexy/evidence.md", body);

        Assert.True(cache.TryGetCovering(conversationId, "comprexy/evidence.md", 1, 80, out _));
        Assert.True(cache.TryGetCovering(conversationId, "comprexy/evidence.md", 80, 80, out _));
        // Previously CoversRange only checked start_line <= cached lines, so 80-180
        // locally "satisfied" with the short tail and the agent looped forever.
        Assert.False(cache.TryGetCovering(conversationId, "comprexy/evidence.md", 80, 180, out _));
        Assert.False(cache.TryGetCovering(conversationId, "comprexy/evidence.md", 81, 180, out _));
    }

    [Fact]
    public void DistillFileRange_StripsReadPaginationFooter_FromCachedBody()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 250 });
        var cache = new ToolIrFileBodyCache(options);
        var distiller = new ToolIrResultDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = new ToolIrCallMapping(
            conversationId,
            "ir_1",
            "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ToolSchemaConstants.FileRangeToolName,
            "read",
            """{"path":"comprexy/evidence.md","start_line":1,"end_line":80}""",
            """{"filePath":"comprexy/evidence.md","offset":1,"limit":80}""",
            "direct",
            "comprexy/evidence.md",
            1,
            80,
            Pending: true);

        var native = """
            <path>/workspace/repo/comprexy/evidence.md</path>
            <type>file</type>
            <content>
            1: # Title
            2: body

            (Showing lines 1-80 of 267. Use offset=81 to continue.)
            </content>
            """;

        var observationJson = distiller.Distill(conversationId, mapping, native);
        using var observation = System.Text.Json.JsonDocument.Parse(observationJson);
        var content = observation.RootElement.GetProperty("content").GetString()!;

        Assert.DoesNotContain("Showing lines", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Use offset=", content, StringComparison.OrdinalIgnoreCase);
        Assert.True(cache.TryGet(conversationId, "comprexy/evidence.md", out var cached));
        Assert.DoesNotContain("Showing lines", cached!.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, ToolIrFileBodyCache.ContentLineCount(cached));
    }

    [Fact]
    public void Invalidate_RemovesRelativeAndAbsoluteAliases()
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            FileCacheAbsoluteExpiration = TimeSpan.FromMinutes(30),
            FileCacheSizeLimit = 64
        });
        var cache = new ToolIrFileBodyCache(options);
        var conversationId = Guid.NewGuid();
        cache.Set(conversationId, "docs/a.md", "old body");

        Assert.Equal(1, cache.Invalidate(conversationId, "/workspace/repo/docs/a.md"));
        Assert.False(cache.TryGet(conversationId, "docs/a.md", out _));
    }
}
