namespace Comprexy.Domain.Enums;

/// <summary>
/// Read-side basis for conversation token proof totals. Persistence and soft-budget math always
/// use tiktoken estimates; this only affects metrics query / report projections.
/// </summary>
public enum PromptTokenBasis
{
    /// <summary>
    /// Report stored tiktoken estimates (<see cref="Entities.ConversationTurnMetric.CompressedInputTokensEstimated"/>).
    /// Savings formulas match the SoftBudget ledger.
    /// </summary>
    Estimated = 0,

    /// <summary>
    /// Prefer upstream <c>usage.prompt_tokens</c> when present. Scales the same-turn raw baseline
    /// by <c>actual / estimate</c> so both arms of a turn share one tokenizer basis; completion
    /// stays <see cref="Entities.ConversationTurnMetric.ActualCompletionTokens"/>.
    /// Default for metrics reads.
    /// </summary>
    ProviderActual = 1
}
