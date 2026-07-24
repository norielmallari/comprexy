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

    [Fact]
    public void Resolve_WithoutHeader_DifferentTimestampSameFingerprint()
    {
        var timestamp1 = "<timestamp>2025-01-01T00:00:00Z</timestamp>";
        var timestamp2 = "<timestamp>2025-01-01T00:01:00Z</timestamp>";

        var messages1 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {timestamp1}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messages2 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {timestamp2}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages1);
        var key2 = _resolver.Resolve(null, messages2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Resolve_WithoutHeader_DifferentORVFSameFingerprint()
    {
        var orvf1 = "<open_and_recently_viewed_files>file1.cs</open_and_recently_viewed_files>";
        var orvf2 = "<open_and_recently_viewed_files>file1.cs\nfile2.cs</open_and_recently_viewed_files>";

        var messages1 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {orvf1}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messages2 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {orvf2}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages1);
        var key2 = _resolver.Resolve(null, messages2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Resolve_WithoutHeader_DifferentAttachedFilesSameFingerprint()
    {
        var attached1 = "<attached_files>image1.png</attached_files>";
        var attached2 = "<attached_files>image1.png\nimage2.png</attached_files>";

        var messages1 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {attached1}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messages2 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {attached2}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages1);
        var key2 = _resolver.Resolve(null, messages2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Resolve_WithoutHeader_DifferentUserQuerySameFingerprint()
    {
        var query1 = "<user_query>what is this?</user_query>";
        var query2 = "<user_query>can you help me?</user_query>";

        var messages1 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {query1}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messages2 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, $"Fix this bug. {query2}"),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages1);
        var key2 = _resolver.Resolve(null, messages2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Resolve_WithoutHeader_DifferentCoreContentDifferentFingerprint()
    {
        var messages1 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Fix this bug."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var messages2 = new List<ChatMessage>
        {
            new(MessageRole.System, "You are a helpful assistant."),
            new(MessageRole.User, "Write a test."),
            new(MessageRole.Assistant, "Sure."),
            new(MessageRole.User, "Also add tests.")
        };

        var key1 = _resolver.Resolve(null, messages1);
        var key2 = _resolver.Resolve(null, messages2);

        Assert.NotEqual(key1, key2);
    }
}
