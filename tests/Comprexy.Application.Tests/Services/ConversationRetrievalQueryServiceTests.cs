using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Retrieval;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class RetrievalQueryLimitsTests
{
    [Fact]
    public void Truncate_LeavesShortTextAndEllipsizesLongText()
    {
        Assert.Equal("short", RetrievalQueryLimits.Truncate("short", 10));
        Assert.Equal("abcdefghij…", RetrievalQueryLimits.Truncate("abcdefghijklmnop", 10));
        Assert.Equal(string.Empty, RetrievalQueryLimits.Truncate(null));
    }
}

public class ConversationRetrievalQueryServiceTests
{
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IConversationMessageRepository> _messages = new();
    private readonly Mock<IWorkingMemoryRepository> _workingMemory = new();
    private readonly ConversationRetrievalQueryService _sut;

    public ConversationRetrievalQueryServiceTests()
    {
        _sut = new ConversationRetrievalQueryService(
            _conversations.Object,
            _messages.Object,
            _workingMemory.Object);
    }

    [Fact]
    public async Task SearchAsync_RanksWorkingMemoryAboveMessagesAndTruncates()
    {
        var conversationId = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _workingMemory.Setup(r => r.SearchContentAsync(
                conversationId,
                "fingerprint",
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                WorkingMemory.Create(
                    conversationId,
                    2,
                    "fingerprint decision in working memory " + new string('x', 600),
                    10,
                    DateTimeOffset.UtcNow)
            ]);
        _messages.Setup(r => r.SearchContentAsync(
                conversationId,
                "fingerprint",
                true,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ConversationMessage.Create(
                    conversationId,
                    5,
                    MessageRole.User,
                    "fingerprint in older message",
                    4,
                    DateTimeOffset.UtcNow),
                ConversationMessage.Create(
                    conversationId,
                    9,
                    MessageRole.Assistant,
                    "fingerprint in newer message",
                    4,
                    DateTimeOffset.UtcNow)
            ]);

        var result = await _sut.SearchAsync(
            conversationId,
            "  fingerprint  ",
            maxResults: 2,
            includeFolded: true,
            includeWorkingMemory: true,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("fingerprint", result!.Query);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("working_memory", result.Matches[0].SourceType);
        Assert.Equal(2, result.Matches[0].WorkingMemoryVersion);
        Assert.EndsWith("…", result.Matches[0].Text);
        Assert.Equal("message", result.Matches[1].SourceType);
        Assert.Equal(9, result.Matches[1].Sequence);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNullWhenConversationMissing_AndRejectsEmptyQuery()
    {
        var missing = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.Null(await _sut.SearchAsync(missing, "x", 10, true, true, CancellationToken.None));

        var present = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(present, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SearchAsync(present, "   ", 10, true, true, CancellationToken.None));
    }

    [Fact]
    public async Task GetMessageWindowAsync_CapsSpanAndOmitsWireJsonByDefault()
    {
        var conversationId = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _messages.Setup(r => r.ListBySequenceRangeAsync(
                conversationId,
                0,
                1,
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ConversationMessage.Create(
                    conversationId,
                    0,
                    MessageRole.User,
                    "a",
                    1,
                    DateTimeOffset.UtcNow,
                    """{"role":"user"}"""),
                ConversationMessage.Create(
                    conversationId,
                    1,
                    MessageRole.Assistant,
                    "b",
                    1,
                    DateTimeOffset.UtcNow,
                    """{"role":"assistant"}""")
            ]);

        var window = await _sut.GetMessageWindowAsync(
            conversationId,
            sequenceStart: 0,
            sequenceEnd: 50,
            includeWireJson: false,
            maxMessages: 2,
            CancellationToken.None);

        Assert.NotNull(window);
        Assert.Equal(2, window!.Count);
        Assert.All(window, m => Assert.Null(m.RawWireJson));
        _messages.Verify(r => r.ListBySequenceRangeAsync(
            conversationId,
            0,
            1,
            2,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOpenToolChainsAsync_ExposesOpenIdsFromUnfoldedHistory()
    {
        var conversationId = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _messages.Setup(r => r.GetUnfoldedAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ConversationMessage.Create(
                    conversationId,
                    0,
                    MessageRole.User,
                    "go",
                    1,
                    DateTimeOffset.UtcNow),
                ConversationMessage.Create(
                    conversationId,
                    1,
                    MessageRole.Assistant,
                    string.Empty,
                    2,
                    DateTimeOffset.UtcNow,
                    """{"role":"assistant","tool_calls":[{"id":"call-1","type":"function","function":{"name":"Read","arguments":"{}"}}]}""")
            ]);

        var chains = await _sut.GetOpenToolChainsAsync(conversationId, CancellationToken.None);

        Assert.NotNull(chains);
        Assert.True(chains!.IsOpen);
        Assert.Equal(["call-1"], chains.OpenToolCallIds);
    }

    [Fact]
    public async Task GetWorkingMemoryAsync_ReturnsLatestOrVersionedSnapshot()
    {
        var conversationId = Guid.NewGuid();
        _conversations.Setup(r => r.ExistsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var latest = WorkingMemory.Create(conversationId, 3, "latest", 5, DateTimeOffset.UtcNow);
        var v1 = WorkingMemory.Create(conversationId, 1, "v1", 3, DateTimeOffset.UtcNow);
        _workingMemory.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);
        _workingMemory.Setup(r => r.GetByVersionAsync(conversationId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(v1);

        var latestDto = await _sut.GetWorkingMemoryAsync(conversationId, version: null, CancellationToken.None);
        var versioned = await _sut.GetWorkingMemoryAsync(conversationId, version: 1, CancellationToken.None);

        Assert.Equal(3, latestDto!.Version);
        Assert.Equal("latest", latestDto.Content);
        Assert.Equal(1, versioned!.Version);
        Assert.Equal("v1", versioned.Content);
    }
}
