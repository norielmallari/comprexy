using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services.ChatTurn;

/// <summary>
/// Metrics-only estimate of IR tools + full unfolded IR transcript without working-memory fold.
/// Does not touch Cache Alignment Prefix or mutate conversation state.
/// </summary>
public sealed class IrFullPromptEstimator
{
    private readonly IRulesInjector _rulesInjector;
    private readonly ContextBuilder _contextBuilder;
    private readonly OutgoingContextMaterializer _contextMaterializer;
    private readonly ITokenEstimator _tokenEstimator;

    public IrFullPromptEstimator(
        IRulesInjector rulesInjector,
        ContextBuilder contextBuilder,
        OutgoingContextMaterializer contextMaterializer,
        ITokenEstimator tokenEstimator)
    {
        _rulesInjector = rulesInjector;
        _contextBuilder = contextBuilder;
        _contextMaterializer = contextMaterializer;
        _tokenEstimator = tokenEstimator;
    }

    /// <summary>
    /// When working memory is null, Prepared is already a full IR rebuild with AllRules — reuse
    /// <paramref name="request"/>.PreparedTokens with no second count. When WM is present, builds
    /// history including folded rows, injects AllRules (<c>hasWorkingMemory: false</c>), and
    /// counts with the same IR tool payload as Prepared.
    /// </summary>
    public int Estimate(IrFullEstimateRequest request)
    {
        if (request.WorkingMemory is null)
        {
            return request.PreparedTokens;
        }

        var irFullPending = _rulesInjector.BuildPendingMessages(
            request.RulesSnapshot,
            hasWorkingMemory: false);

        var recentRaw = _contextMaterializer.PrepareRecentRawForChatTemplate(
            request.ConversationId,
            request.AllMessages
                .Where(m => m.Sequence < request.TipEntity.Sequence)
                .OrderBy(m => m.Sequence)
                .ToList(),
            request.TipMessage,
            request.AllMessages,
            request.TipEntity.Sequence,
            applyLiveDedupe: true);

        var irFullMessages = _contextBuilder.Build(
            request.SystemPrompt,
            workingMemory: null,
            recentRaw,
            request.TipMessage,
            irFullPending);

        irFullMessages = _contextMaterializer.EnsureOutgoingEndsAtTip(
            request.ConversationId,
            irFullMessages,
            request.TipMessage,
            request.TipEntity.Sequence);

        return _tokenEstimator.CountPromptTokens(irFullMessages, request.EstimatePayload);
    }
}

/// <summary>
/// Inputs for a metrics-only IrFull prompt estimate. Carries <see cref="RulesSnapshot"/> so the
/// estimator can inject AllRules — never Prepared's PendingRules-only list.
/// </summary>
public sealed record IrFullEstimateRequest(
    Guid ConversationId,
    string? SystemPrompt,
    RulesSnapshot RulesSnapshot,
    IReadOnlyList<ConversationMessage> AllMessages,
    ConversationMessage TipEntity,
    ChatMessage TipMessage,
    WorkingMemory? WorkingMemory,
    int PreparedTokens,
    JsonElement? EstimatePayload);
