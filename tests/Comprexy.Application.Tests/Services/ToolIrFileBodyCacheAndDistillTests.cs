using System.Text.Json;
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
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
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
    public void Distill_Shell_TruncatesContentAndDoesNotTouchFileCache()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxShellObservationChars = 20 });
        var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = new ToolIrCallMapping(
            conversationId,
            "ir_shell",
            "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ToolSchemaConstants.ShellToolName,
            "Shell",
            """{"command":"ls"}""",
            """{"command":"ls"}""",
            "direct",
            Path: null,
            StartLine: null,
            EndLine: null,
            Pending: true);

        var observationJson = distiller.Distill(conversationId, mapping, new string('z', 50));
        using var observation = System.Text.Json.JsonDocument.Parse(observationJson);

        Assert.Equal("shell", observation.RootElement.GetProperty("type").GetString());
        Assert.Equal("ls", observation.RootElement.GetProperty("command").GetString());
        Assert.True(observation.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(21, observation.RootElement.GetProperty("content").GetString()!.Length);
        Assert.False(cache.TryGet(conversationId, "ls", out _));
    }

    [Fact]
    public void DistillFileRange_PartialWindow_DoesNotPoisonCache_AndReturnsContent()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 200 });
        var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
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
        cache.Set(conversationId, "docs/a.md", "l1\nl2\nl3\nl4\nl5\n", bodyComplete: true);

        var kept = cache.SetIfRicher(conversationId, "docs/a.md", "partial\n", bodyComplete: true);

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
        cache.Set(conversationId, "docs/a.md", "---\n\n## Theme Support\nbody\n", bodyComplete: true);

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
        cache.Set(conversationId, "comprexy/evidence.md", body, bodyComplete: true);

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
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
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
        cache.Set(conversationId, "docs/a.md", "old body", bodyComplete: true);

        Assert.Equal(1, cache.Invalidate(conversationId, "/workspace/repo/docs/a.md"));
        Assert.False(cache.TryGet(conversationId, "docs/a.md", out _));
    }

    [Fact]
    public void DistillFileSearch_JsonStringWrappedResult_KeepsEveryMatch()
    {
        using var observation = DistillFileSearch(JsonSerializer.Serialize(SearchResultLines));
        var root = observation.RootElement;

        Assert.Equal("file_search", root.GetProperty("type").GetString());
        Assert.Equal(3, root.GetProperty("match_count").GetInt32());
        Assert.Equal(3, root.GetProperty("total_match_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());

        var first = MatchAt(root, 0);
        Assert.Equal("src/alpha.py", first.Path);
        Assert.Equal(12, first.Line);
        Assert.Equal("def f():", first.Preview);

        foreach (var match in root.GetProperty("matches").EnumerateArray())
        {
            var preview = match.GetProperty("preview").GetString()!;
            Assert.DoesNotContain("\\n", preview, StringComparison.Ordinal);
            Assert.DoesNotContain("\"", preview, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DistillFileSearch_RawMultilineText_ParsesPathAndLine()
    {
        using var observation = DistillFileSearch(SearchResultLines);
        var root = observation.RootElement;

        Assert.Equal(3, root.GetProperty("match_count").GetInt32());
        Assert.Equal(3, root.GetProperty("total_match_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(("src/alpha.py", 12, "def f():"), MatchAt(root, 0));
        Assert.Equal(("src/beta.py", 3, "x = 1"), MatchAt(root, 1));
        Assert.Equal(("docs/notes.md", 7, "note"), MatchAt(root, 2));
    }

    [Fact]
    public void DistillFileSearch_JsonObjectResult_ProjectsMatchesArray()
    {
        using var observation = DistillFileSearch(
            """{"matches":[{"path":"src/alpha.py","line":3,"preview":"x"}]}""");
        var root = observation.RootElement;

        Assert.Equal(1, root.GetProperty("match_count").GetInt32());
        Assert.Equal(1, root.GetProperty("total_match_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(("src/alpha.py", 3, "x"), MatchAt(root, 0));
    }

    [Fact]
    public void DistillFileSearch_JsonStringWrappedObject_UnwrapsToMatchesArray()
    {
        var native = JsonSerializer.Serialize(
            JsonSerializer.Serialize(new { matches = new[] { new { path = "src/alpha.py", line = 3, preview = "x" } } }));

        using var observation = DistillFileSearch(native);
        var root = observation.RootElement;

        Assert.Equal(1, root.GetProperty("match_count").GetInt32());
        Assert.Equal(1, root.GetProperty("total_match_count").GetInt32());
        Assert.Equal(("src/alpha.py", 3, "x"), MatchAt(root, 0));
    }

    [Fact]
    public void DistillFileSearch_UnwrapDepthExceeded_KeepsPayloadAndFlagsCut()
    {
        var text = string.Concat(Enumerable.Repeat("alpha beta gamma delta ", 15));
        Assert.True(text.Length > 200);
        var native = JsonSerializer.Serialize(JsonSerializer.Serialize(JsonSerializer.Serialize(text)));

        using var observation = DistillFileSearch(native);
        var root = observation.RootElement;

        Assert.Equal(1, root.GetProperty("match_count").GetInt32());
        Assert.Equal(1, root.GetProperty("total_match_count").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());

        var only = MatchAt(root, 0);
        Assert.Equal(string.Empty, only.Path);
        Assert.Equal(0, only.Line);
        Assert.Equal(201, only.Preview.Length);
    }

    [Fact]
    public void DistillFileSearch_LongMatchText_SetsTruncatedWhenPreviewCut()
    {
        using var observation = DistillFileSearch("src/alpha.py:12: " + new string('a', 300));
        var root = observation.RootElement;

        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, root.GetProperty("match_count").GetInt32());
        Assert.Equal(1, root.GetProperty("total_match_count").GetInt32());

        var only = MatchAt(root, 0);
        Assert.Equal("src/alpha.py", only.Path);
        Assert.Equal(12, only.Line);
        Assert.Equal(201, only.Preview.Length);
        Assert.EndsWith("…", only.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void DistillFileSearch_JsonElementLongPreview_SetsTruncated()
    {
        var native = JsonSerializer.Serialize(
            new { matches = new[] { new { path = "src/alpha.py", line = 3, preview = new string('a', 300) } } });

        using var observation = DistillFileSearch(native);
        var root = observation.RootElement;

        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, root.GetProperty("match_count").GetInt32());
        Assert.Equal(1, root.GetProperty("total_match_count").GetInt32());
        Assert.Equal(201, MatchAt(root, 0).Preview.Length);
    }

    [Fact]
    public void DistillFileSearch_MoreMatchesThanCap_ReportsPreCapTotal()
    {
        var native = string.Join(
            '\n',
            Enumerable.Range(1, 5).Select(i => $"src/mod{i}.py:{i}: hit {i}"));

        using var observation = DistillFileSearch(native, maxSearchMatches: 2);
        var root = observation.RootElement;

        Assert.Equal(2, root.GetProperty("match_count").GetInt32());
        Assert.Equal(5, root.GetProperty("total_match_count").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void DistillFileSearch_NonConformingLines_DegradeToPreviewOnly()
    {
        const string native = "src/alpha.py:12: def f():\nnote: 12: hello\nno separator on this line";

        using var observation = DistillFileSearch(native);
        var root = observation.RootElement;

        Assert.Equal(3, root.GetProperty("match_count").GetInt32());
        Assert.Equal(3, root.GetProperty("total_match_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(("src/alpha.py", 12, "def f():"), MatchAt(root, 0));
        Assert.Equal((string.Empty, 0, "note: 12: hello"), MatchAt(root, 1));
        Assert.Equal((string.Empty, 0, "no separator on this line"), MatchAt(root, 2));
    }

    [Fact]
    public void DistillFileSearch_WindowsDrivePath_ParsesPathAndLine()
    {
        const string native = @"C:\ws\src\alpha.cs:12: x" + "\nweird:name.py:3: x";

        using var observation = DistillFileSearch(native);
        var root = observation.RootElement;

        Assert.Equal(2, root.GetProperty("match_count").GetInt32());
        Assert.Equal(("C:/ws/src/alpha.cs", 12, "x"), MatchAt(root, 0));
        Assert.Equal(("weird:name.py", 3, "x"), MatchAt(root, 1));
    }

    [Fact]
    public void DistillDirList_JsonStringWrappedResult_ListsEveryEntry()
    {
        var options = Options.Create(new ToolSchemaOptions());
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.DirListToolName,
            """{"path":"src"}""",
            path: "src");

        var native = JsonSerializer.Serialize("alpha.txt\nbeta/\ngamma.md");
        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        var root = observation.RootElement;

        Assert.Equal("dir_list", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(3, root.GetProperty("entry_count").GetInt32());

        var names = root.GetProperty("entries")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToArray();
        Assert.Equal(new[] { "alpha.txt", "beta/", "gamma.md" }, names);
        Assert.All(
            root.GetProperty("entries").EnumerateArray(),
            e => Assert.Equal("unknown", e.GetProperty("kind").GetString()));
    }

    [Fact]
    public void DistillFileRange_JsonStringWrappedBody_DoesNotPoisonCache()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 250 });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"src/alpha.py","start_line":1,"end_line":3}""",
            path: "src/alpha.py",
            startLine: 1,
            endLine: 3);

        var native = JsonSerializer.Serialize(
            "<content>\n1: line one\n2: line two\n3: line three\n</content>");

        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        var content = observation.RootElement.GetProperty("content").GetString()!;

        Assert.Equal("line one\nline two\nline three", content);
        Assert.DoesNotContain("\\n", content, StringComparison.Ordinal);
        Assert.True(cache.TryGet(conversationId, "src/alpha.py", out var entry));
        Assert.Equal(3, ToolIrFileBodyCache.ContentLineCount(entry!));
        Assert.True(cache.TryGetCovering(conversationId, "src/alpha.py", 1, 3, out _));
    }

    [Fact]
    public void DistillFileManifest_JsonStringWrappedBody_CountsDecodedLines()
    {
        var options = Options.Create(new ToolSchemaOptions());
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileManifestToolName,
            """{"path":"src/alpha.py"}""",
            path: "src/alpha.py");

        var native = JsonSerializer.Serialize(
            "<content>\n1: import os\n2: def f():\n3:     return 1\n</content>");

        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        var root = observation.RootElement;

        Assert.Equal("file_manifest", root.GetProperty("type").GetString());
        Assert.Equal("python", root.GetProperty("language").GetString());
        Assert.Equal(
            new[] { "import os" },
            root.GetProperty("imports").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal("f", root.GetProperty("symbols")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Distill_Shell_JsonStringWrappedOutput_DecodesBeforeCap()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxShellObservationChars = 4000 });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.ShellToolName,
            """{"command":"ls"}""");

        var native = JsonSerializer.Serialize("line one\nline two");
        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        var root = observation.RootElement;

        Assert.Equal("shell", root.GetProperty("type").GetString());
        Assert.Equal("line one\nline two", root.GetProperty("content").GetString());
        Assert.False(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void Distill_UnmappedTool_JsonStringWrappedContent_FlagsTruncation()
    {
        var options = Options.Create(new ToolSchemaOptions());
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(conversationId, "client_custom_tool", """{"input":"x"}""");

        var raw = string.Join(
            '\n',
            Enumerable.Range(1, 400).Select(i => $"observation line {i} padding padding"));
        Assert.True(raw.Length > 4000);

        using var observation = JsonDocument.Parse(
            distiller.Distill(conversationId, mapping, JsonSerializer.Serialize(raw)));
        var root = observation.RootElement;

        Assert.Equal("passthrough", root.GetProperty("type").GetString());
        Assert.Equal("client_custom_tool", root.GetProperty("tool").GetString());
        Assert.True(root.TryGetProperty("truncated", out var truncated));
        Assert.True(truncated.GetBoolean());

        var content = root.GetProperty("content").GetString()!;
        Assert.Equal(4001, content.Length);
        Assert.EndsWith("…", content, StringComparison.Ordinal);
        Assert.Contains("\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n", content, StringComparison.Ordinal);
    }


    [Fact]
    public void EnvelopeGate_EmbeddedContentInCode_NotUnwrapped_RealEnvelopeIs()
    {
        Assert.False(ToolIrResultDistiller.TryExtractTaggedContent(
            "var x = \"<content>fragment</content>\";", out _));
        Assert.True(ToolIrResultDistiller.TryExtractTaggedContent(
            "<path>docs/a.md</path><type>file</type><content>hello\nworld</content>", out var body));
        Assert.Equal("hello\nworld", body);
    }

    [Fact]
    public void DistillFileRange_FooterPrefix_CachesIncompleteWithTotal()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 250 });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1,"end_line":80}""",
            path: "docs/a.md",
            startLine: 1,
            endLine: 80);

        var lines = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"{i}: line-{i}"));
        var native = $"<path>docs/a.md</path><type>file</type><content>\n{lines}\n\n(Showing lines 1-80 of 267. Use offset=81 to continue.)\n</content>";
        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        Assert.False(observation.RootElement.GetProperty("complete").GetBoolean());
        Assert.False(observation.RootElement.GetProperty("body_complete").GetBoolean());
        Assert.Equal(267, observation.RootElement.GetProperty("total_line_count").GetInt32());
        Assert.Equal(81, observation.RootElement.GetProperty("next_start_line").GetInt32());
        Assert.True(cache.TryGet(conversationId, "docs/a.md", out var entry));
        Assert.False(entry!.BodyComplete);
        Assert.Equal(267, entry.TotalLineCount);
        Assert.DoesNotContain("Showing lines", entry.Body, StringComparison.OrdinalIgnoreCase);
        Assert.False(cache.TryGetCovering(conversationId, "docs/a.md", 10, 40, out _));
    }

    [Fact]
    public void DistillFileRange_BodyComplete_ReflectsReturnedRicherEntry()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxRangeLines = 250 });
        using var cache = new ToolIrFileBodyCache(options);
        var conversationId = Guid.NewGuid();
        var full = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line-{i}")) + "\n";
        cache.Set(conversationId, "docs/a.md", full, bodyComplete: true, totalLineCount: 100);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1,"end_line":10}""",
            path: "docs/a.md",
            startLine: 1,
            endLine: 10);
        var window = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"{i}: window-{i}"));
        var native = $"<path>docs/a.md</path><content>\n{window}\n\n(Showing lines 1-10 of 267. Use offset=11 to continue.)\n</content>";
        using var observation = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        Assert.True(observation.RootElement.GetProperty("body_complete").GetBoolean());
        Assert.Contains("line-1", observation.RootElement.GetProperty("content").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("window-1", observation.RootElement.GetProperty("content").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void DistillFileRange_FirstReadCaps_LinesAndChars()
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            FirstReadMaxLines = 5,
            FirstReadMaxChars = 60000,
            MaxRangeLines = 250
        });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var body = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line-{i}"));
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1}""",
            path: "docs/a.md",
            startLine: 1,
            endLine: null);
        var native = $"<path>docs/a.md</path><content>\n{body}\n</content>";
        using var obs = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        Assert.True(obs.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(5, obs.RootElement.GetProperty("content").GetString()!.Split('\n').Length);

        var charOptions = Options.Create(new ToolSchemaOptions
        {
            FirstReadMaxLines = 400,
            FirstReadMaxChars = 20,
            MaxRangeLines = 250
        });
        using var cache2 = new ToolIrFileBodyCache(charOptions);
        var distiller2 = ToolIrTestFactory.CreateDistiller(charOptions, cache2);
        using var obs2 = JsonDocument.Parse(distiller2.Distill(conversationId, mapping with { ConversationId = conversationId }, native));
        Assert.True(obs2.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(obs2.RootElement.GetProperty("content").GetString()!.Length <= 21);

        var windowed = Options.Create(new ToolSchemaOptions { MaxRangeLines = 3, FirstReadMaxLines = 400 });
        using var cache3 = new ToolIrFileBodyCache(windowed);
        var distiller3 = ToolIrTestFactory.CreateDistiller(windowed, cache3);
        var mappingWindowed = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1,"end_line":40}""",
            path: "docs/a.md",
            startLine: 1,
            endLine: 40);
        using var obs3 = JsonDocument.Parse(distiller3.Distill(conversationId, mappingWindowed, native));
        Assert.True(obs3.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(3, obs3.RootElement.GetProperty("content").GetString()!.Split('\n').Length);
    }

    [Fact]
    public void DistillFileManifest_LineCountAndTruncationFlags()
    {
        var body = "using A;\nusing B;\nusing C;\npublic class Foo {}\npublic class Bar {}\n";
        var entry = ToolIrFileBodyCache.BuildEntry("src/Foo.cs", body, bodyComplete: true, totalLineCount: null);
        using var json = JsonDocument.Parse(
            ToolIrResultDistiller.BuildManifestFromCache(entry, maxImports: 2, maxSymbols: 1, maxImportChars: 160));
        Assert.Equal(ToolIrFileBodyCache.ContentLineCount(entry), json.RootElement.GetProperty("line_count").GetInt32());
        Assert.True(json.RootElement.GetProperty("body_complete").GetBoolean());
        Assert.True(json.RootElement.GetProperty("imports_truncated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("symbols_truncated").GetBoolean());

        using var below = JsonDocument.Parse(
            ToolIrResultDistiller.BuildManifestFromCache(entry, maxImports: 20, maxSymbols: 30, maxImportChars: 160));
        Assert.False(below.RootElement.GetProperty("imports_truncated").GetBoolean());
        Assert.False(below.RootElement.GetProperty("symbols_truncated").GetBoolean());

        var incomplete = ToolIrFileBodyCache.BuildEntry("src/Foo.cs", body, bodyComplete: false, totalLineCount: 99);
        using var win = JsonDocument.Parse(ToolIrResultDistiller.BuildManifestFromCache(incomplete, 20, 30, 160));
        Assert.False(win.RootElement.GetProperty("body_complete").GetBoolean());
    }

    [Fact]
    public void DistillFileSearch_SplitFlags_AndSentinels()
    {
        using var cap = DistillFileSearch(
            string.Join('\n', Enumerable.Range(1, 5).Select(i => $"src/a.cs:{i}: hit-{i}")),
            maxSearchMatches: 2);
        Assert.True(cap.RootElement.GetProperty("matches_truncated").GetBoolean());
        Assert.False(cap.RootElement.GetProperty("preview_truncated").GetBoolean());
        Assert.True(cap.RootElement.GetProperty("truncated").GetBoolean());

        var options = Options.Create(new ToolSchemaOptions { MaxSearchMatches = 40, MaxSearchPreviewChars = 5 });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(conversationId, ToolSchemaConstants.FileSearchToolName, """{"query":"q"}""", path: "src");
        using var preview = JsonDocument.Parse(distiller.Distill(conversationId, mapping, "src/a.cs:1: long-preview-text"));
        Assert.False(preview.RootElement.GetProperty("matches_truncated").GetBoolean());
        Assert.True(preview.RootElement.GetProperty("preview_truncated").GetBoolean());
        Assert.True(preview.RootElement.GetProperty("truncated").GetBoolean());

        using var none = DistillFileSearch("No matches found");
        Assert.Equal(0, none.RootElement.GetProperty("match_count").GetInt32());
        Assert.Equal(0, none.RootElement.GetProperty("total_match_count").GetInt32());
        Assert.Equal("no_matches", none.RootElement.GetProperty("status").GetString());

        using var err = DistillFileSearch("Error: search failed");
        Assert.Equal(0, err.RootElement.GetProperty("match_count").GetInt32());
        Assert.Equal("error", err.RootElement.GetProperty("status").GetString());

        using var unstructured = DistillFileSearch("alpha result\nbeta result\ngamma result");
        Assert.Equal("unstructured", unstructured.RootElement.GetProperty("parse_mode").GetString());
        Assert.Equal(3, unstructured.RootElement.GetProperty("match_count").GetInt32());
    }

    [Fact]
    public void DistillDirList_TotalEntryCount_PreCap()
    {
        var options = Options.Create(new ToolSchemaOptions { MaxDirListEntries = 2 });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(conversationId, ToolSchemaConstants.DirListToolName, """{"path":"src"}""", path: "src");
        var native = JsonSerializer.Serialize(new { entries = new[] { "a", "b", "c", "d" } });
        using var obs = JsonDocument.Parse(distiller.Distill(conversationId, mapping, native));
        Assert.Equal(2, obs.RootElement.GetProperty("entry_count").GetInt32());
        Assert.Equal(4, obs.RootElement.GetProperty("total_entry_count").GetInt32());
        Assert.True(obs.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void Distill_CapsFromOptions_SearchPreviewAndPassthrough()
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            MaxSearchPreviewChars = 4,
            MaxPassthroughObservationChars = 6
        });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var search = BuildMapping(conversationId, ToolSchemaConstants.FileSearchToolName, """{"query":"q"}""");
        using var s = JsonDocument.Parse(distiller.Distill(conversationId, search, "src/a.cs:1: abcdefgh"));
        Assert.True(s.RootElement.GetProperty("preview_truncated").GetBoolean());
        Assert.True(s.RootElement.GetProperty("matches")[0].GetProperty("preview").GetString()!.StartsWith("abcd", StringComparison.Ordinal));

        var pass = BuildMapping(conversationId, "client_custom_tool", "{}");
        using var p = JsonDocument.Parse(distiller.Distill(conversationId, pass, "1234567890"));
        Assert.True(p.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(7, p.RootElement.GetProperty("content").GetString()!.Length);
    }

    private const string SearchResultLines =
        "src/alpha.py:12: def f():\nsrc/beta.py:3: x = 1\ndocs/notes.md:7: note";

    private static JsonDocument DistillFileSearch(string nativeContent, int maxSearchMatches = 40)
    {
        var options = Options.Create(new ToolSchemaOptions { MaxSearchMatches = maxSearchMatches });
        using var cache = new ToolIrFileBodyCache(options);
        var distiller = ToolIrTestFactory.CreateDistiller(options, cache);
        var conversationId = Guid.NewGuid();
        var mapping = BuildMapping(
            conversationId,
            ToolSchemaConstants.FileSearchToolName,
            """{"query":"alpha"}""",
            path: "src");

        return JsonDocument.Parse(distiller.Distill(conversationId, mapping, nativeContent));
    }

    private static ToolIrCallMapping BuildMapping(
        Guid conversationId,
        string comprexyToolName,
        string irArgumentsJson,
        string? path = null,
        int? startLine = null,
        int? endLine = null) =>
        new(
            conversationId,
            "call_1",
            "cur_1",
            comprexyToolName,
            "client_tool",
            irArgumentsJson,
            ClientArgumentsJson: null,
            Strategy: "direct",
            Path: path,
            StartLine: startLine,
            EndLine: endLine,
            Pending: false);

    private static (string Path, int Line, string Preview) MatchAt(JsonElement root, int index)
    {
        var match = root.GetProperty("matches")[index];
        return (
            match.GetProperty("path").GetString()!,
            match.GetProperty("line").GetInt32(),
            match.GetProperty("preview").GetString()!);
    }
}
