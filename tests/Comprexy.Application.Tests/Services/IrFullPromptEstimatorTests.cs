using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class IrFullPromptEstimatorTests
{
    [Fact]
    public void Estimate_WhenWorkingMemoryNull_ReturnsPreparedTokensWithoutRecount()
    {
        var rulesInjector = new Mock<IRulesInjector>(MockBehavior.Strict);
        var tokenEstimator = new Mock<ITokenEstimator>(MockBehavior.Strict);
        var cacheAlignment = new Mock<ICacheAlignmentService>(MockBehavior.Strict);
        var estimator = CreateEstimator(rulesInjector.Object, tokenEstimator.Object, cacheAlignment.Object);

        var conversationId = Guid.NewGuid();
        var tip = ConversationMessage.Create(
            conversationId,
            sequence: 0,
            MessageRole.User,
            "tip",
            tokenCount: 3,
            DateTimeOffset.UnixEpoch);
        var tipMessage = new ChatMessage(MessageRole.User, "tip");
        var request = new IrFullEstimateRequest(
            conversationId,
            SystemPrompt: "system",
            RulesSnapshot: EmptySnapshot(),
            AllMessages: [tip],
            TipEntity: tip,
            TipMessage: tipMessage,
            WorkingMemory: null,
            PreparedTokens: 12_345,
            EstimatePayload: null);

        var result = estimator.Estimate(request);

        Assert.Equal(12_345, result);
        rulesInjector.VerifyNoOtherCalls();
        tokenEstimator.VerifyNoOtherCalls();
        cacheAlignment.VerifyNoOtherCalls();
    }

    [Fact]
    public void Estimate_WhenWorkingMemoryPresent_BuildsWithoutWm_CountsIrPayload_UsesAllRulesArm()
    {
        var conversationId = Guid.NewGuid();
        var allRuleMessages = new List<ChatMessage>
        {
            new(MessageRole.System, "ALL_RULE_BODY_A"),
            new(MessageRole.System, "ALL_RULE_BODY_B")
        };
        var rulesInjector = new Mock<IRulesInjector>(MockBehavior.Strict);
        rulesInjector
            .Setup(x => x.BuildPendingMessages(It.IsAny<RulesSnapshot>(), false))
            .Returns(allRuleMessages);

        IReadOnlyList<ChatMessage>? countedMessages = null;
        JsonElement? countedPayload = null;
        var tokenEstimator = new Mock<ITokenEstimator>(MockBehavior.Strict);
        tokenEstimator
            .Setup(x => x.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Callback<IEnumerable<ChatMessage>, JsonElement?>((messages, payload) =>
            {
                countedMessages = messages.ToList();
                countedPayload = payload;
            })
            .Returns(4_242);

        var cacheAlignment = new Mock<ICacheAlignmentService>(MockBehavior.Strict);
        var estimator = CreateEstimator(rulesInjector.Object, tokenEstimator.Object, cacheAlignment.Object);

        var history = ConversationMessage.Create(
            conversationId,
            sequence: 0,
            MessageRole.User,
            "prior user",
            tokenCount: 4,
            DateTimeOffset.UnixEpoch);
        var tip = ConversationMessage.Create(
            conversationId,
            sequence: 1,
            MessageRole.User,
            "tip user",
            tokenCount: 3,
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        var tipMessage = new ChatMessage(MessageRole.User, "tip user");
        var workingMemory = WorkingMemory.Create(
            conversationId,
            version: 1,
            content: "folded summary",
            tokenCount: 50,
            DateTimeOffset.UnixEpoch);
        using var payloadDoc = JsonDocument.Parse("""{"tools":[{"type":"function","function":{"name":"demo"}}]}""");
        var estimatePayload = payloadDoc.RootElement.Clone();
        var snapshot = new RulesSnapshot(
            "base",
            [
                new RuleBlock("a.md", "a.md", "A", RuleSource.System),
                new RuleBlock("b.md", "b.md", "B", RuleSource.System)
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.md" },
            [new RuleBlock("b.md", "b.md", "B", RuleSource.System)]);

        var result = estimator.Estimate(new IrFullEstimateRequest(
            conversationId,
            SystemPrompt: "system prompt",
            RulesSnapshot: snapshot,
            AllMessages: [history, tip],
            TipEntity: tip,
            TipMessage: tipMessage,
            WorkingMemory: workingMemory,
            PreparedTokens: 9_999,
            EstimatePayload: estimatePayload));

        Assert.Equal(4_242, result);
        rulesInjector.Verify(
            x => x.BuildPendingMessages(It.IsAny<RulesSnapshot>(), false),
            Times.Once);
        rulesInjector.Verify(
            x => x.BuildPendingMessages(It.IsAny<RulesSnapshot>(), true),
            Times.Never);
        tokenEstimator.Verify(
            x => x.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()),
            Times.Once);
        Assert.NotNull(countedMessages);
        Assert.Contains(countedMessages!, m => m.Content.Contains("ALL_RULE_BODY_A", StringComparison.Ordinal));
        Assert.Contains(countedMessages!, m => m.Content.Contains("ALL_RULE_BODY_B", StringComparison.Ordinal));
        Assert.DoesNotContain(countedMessages!, m => m.Content.Contains("folded summary", StringComparison.Ordinal));
        Assert.NotNull(countedPayload);
        Assert.Equal(estimatePayload.GetRawText(), countedPayload!.Value.GetRawText());
        cacheAlignment.VerifyNoOtherCalls();
    }

    private static IrFullPromptEstimator CreateEstimator(
        IRulesInjector rulesInjector,
        ITokenEstimator tokenEstimator,
        ICacheAlignmentService cacheAlignment)
    {
        var contextBuilder = new ContextBuilder();
        var materializer = new OutgoingContextMaterializer(
            contextBuilder,
            cacheAlignment,
            Options.Create(new ContextPolicyOptions()),
            Options.Create(new CacheAlignmentOptions()),
            NullLogger<OutgoingContextMaterializer>.Instance);
        return new IrFullPromptEstimator(
            rulesInjector,
            contextBuilder,
            materializer,
            tokenEstimator);
    }

    private static RulesSnapshot EmptySnapshot() =>
        new("base", [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
}
