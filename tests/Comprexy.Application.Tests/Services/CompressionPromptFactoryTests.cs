using Comprexy.Application.Services;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class CompressionPromptFactoryTests
{
    private readonly CompressionPromptFactory _factory = new(
        "inline instruction",
        """
        # Working Memory

        ## Current Goal
        ...
        """);

    [Fact]
    public void BuildInlineWrapUpUserMessage_IncludesInstructionAndTemplate()
    {
        var message = _factory.BuildInlineWrapUpUserMessage();

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Contains("inline instruction", message.Content);
        Assert.Contains("# Working Memory", message.Content);
        Assert.Contains("## Current Goal", message.Content);
    }

    [Fact]
    public void Constructor_RequiresInlineInstruction()
    {
        Assert.Throws<ArgumentException>(() => new CompressionPromptFactory("  "));
    }
}
