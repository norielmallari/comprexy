using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ConversationIdentityResolverTests
{
    private readonly ConversationIdentityResolver _resolver = new();

    [Fact]
    public void Resolve_WithHeader_ReturnsHeaderBasedKey()
    {
        var messages = new List<ChatMessage> { new(MessageRole.User, "hello") };

        var key = _resolver.Resolve("my-conversation-123", messages);

        Assert.Equal("header:my-conversation-123", key);
    }

    [Fact]
    public void Resolve_WithoutHeader_IsDeterministicForSameMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages);
        var key2 = _resolver.Resolve(string.Empty, messages);

        Assert.Equal(key1, key2);
        Assert.StartsWith("fingerprint:", key1);
    }

    [Fact]
    public void Resolve_WithoutHeader_DiffersForDifferentFirstUserMessage()
    {
        var messagesA = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.User, "Also add tests.")
        };

        var messagesB = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Write a test."),
            new(MessageRole.User, "Also add tests.")
        };

        var keyA = _resolver.Resolve(null, messagesA);
        var keyB = _resolver.Resolve(null, messagesB);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Resolve_WithoutHeader_DiffersForDifferentSecondUserMessage()
    {
        var messagesA = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messagesB = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also refactor.")
        };

        var keyA = _resolver.Resolve(null, messagesA);
        var keyB = _resolver.Resolve(null, messagesB);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Resolve_WithoutHeader_IgnoresUserMessagesBeyondTheSecond()
    {
        var messagesA = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests."),
            new(MessageRole.Assistant, "Done."),
            new(MessageRole.User, "Ship it.")
        };

        var messagesB = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests."),
            new(MessageRole.Assistant, "Done."),
            new(MessageRole.User, "Never mind.")
        };

        var keyA = _resolver.Resolve(null, messagesA);
        var keyB = _resolver.Resolve(null, messagesB);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void Resolve_WithoutHeaderOrSystemMessage_StillProducesFingerprint()
    {
        var messages = new List<ChatMessage> { new(MessageRole.User, "hello") };

        var key = _resolver.Resolve(null, messages);

        Assert.StartsWith("fingerprint:", key);
    }
}
