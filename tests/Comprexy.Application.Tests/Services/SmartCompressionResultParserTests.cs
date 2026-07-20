using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class SmartCompressionResultParserTests
{
    [Fact]
    public void Parse_MarkdownWithRetainSection_ExtractsSequencesAndStripsSectionFromWm()
    {
        var result = SmartCompressionResultParser.Parse("""
            # Working Memory
            ## Current Goal
            Ship it

            ## Retain Sequences
            2, 5, 5, 7
            """);

        Assert.True(result.FoundRetainSection);
        Assert.True(result.HasRetainList);
        Assert.True(result.HasWorkingMemory);
        Assert.Contains("Ship it", result.WorkingMemory);
        Assert.DoesNotContain("Retain Sequences", result.WorkingMemory);
        Assert.Equal([2, 5, 5, 7], result.RetainSequences);
    }

    [Fact]
    public void Parse_BulletRetainList_IsAccepted()
    {
        var result = SmartCompressionResultParser.Parse("""
            # Working Memory
            ## Current Goal
            Done

            ## Retain Sequences
            - 1
            - 4
            """);

        Assert.True(result.HasRetainList);
        Assert.Equal([1, 4], result.RetainSequences);
        Assert.DoesNotContain("Retain Sequences", result.WorkingMemory);
    }

    [Fact]
    public void Parse_MarkdownWithoutRetainSection_IsMarkdownOnlyFallback()
    {
        var result = SmartCompressionResultParser.Parse("# Working Memory\n## Current Goal\nDone");

        Assert.False(result.FoundRetainSection);
        Assert.False(result.HasRetainList);
        Assert.True(result.HasWorkingMemory);
        Assert.Null(result.RetainSequences);
        Assert.Contains("Done", result.WorkingMemory);
    }

    [Fact]
    public void Parse_EmptyRetainSection_StillCountsAsFoundList()
    {
        var result = SmartCompressionResultParser.Parse("""
            # Working Memory
            ## Current Goal
            Done

            ## Retain Sequences
            """);

        Assert.True(result.FoundRetainSection);
        Assert.True(result.HasRetainList);
        Assert.Empty(result.RetainSequences!);
        Assert.Contains("Done", result.WorkingMemory);
    }

    [Fact]
    public void Parse_RetainSectionOnly_IsEmpty()
    {
        var result = SmartCompressionResultParser.Parse("""
            ## Retain Sequences
            1, 2
            """);

        Assert.False(result.HasWorkingMemory);
    }
}
