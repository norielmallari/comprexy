using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class WorkingMemorySanityCheckerTests
{
    [Theory]
    [InlineData("# Working Memory\n## Current Goal\nShip it")]
    [InlineData("# working memory\n## Persona\nSenior engineer")]
    [InlineData("```markdown\n# Working Memory\n## Current Goal\nDone\n```")]
    [InlineData("```\n# Working Memory\n## Active Task\nFix tests\n```")]
    public void TryAccept_ValidStructure_ReturnsNormalized(string content)
    {
        Assert.True(WorkingMemorySanityChecker.TryAccept(content, out var normalized, out var reason));
        Assert.Equal(string.Empty, reason);
        Assert.Contains("# Working Memory", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```", normalized);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("Sorry, I cannot help with that.", "missing_working_memory_heading")]
    [InlineData("# Summary\n## Goal\nNope", "missing_working_memory_heading")]
    [InlineData("# Working Memory\nJust prose without sections.", "missing_section_heading")]
    [InlineData("```markdown\n\n```", "empty_after_normalize")]
    public void TryAccept_InvalidStructure_RejectsWithReason(string? content, string expectedReason)
    {
        Assert.False(WorkingMemorySanityChecker.TryAccept(content, out var normalized, out var reason));
        Assert.Equal(expectedReason, reason);
        Assert.Equal(string.Empty, normalized);
    }
}
