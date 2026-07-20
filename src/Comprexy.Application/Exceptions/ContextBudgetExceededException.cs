namespace Comprexy.Application.Exceptions;

/// <summary>
/// Raised when the outgoing prompt is still at or above <c>HardLimitTokens</c> after emergency
/// compression (and any send-time retain trim). Mapped to HTTP 413 by the API.
/// </summary>
public sealed class ContextBudgetExceededException : Exception
{
    public ContextBudgetExceededException(
        Guid conversationId,
        int estimatedTokens,
        int hardLimitTokens)
        : base(
            $"Conversation {conversationId} context estimate ({estimatedTokens} tokens) is still at or above the hard limit ({hardLimitTokens} tokens) after emergency compression.")
    {
        ConversationId = conversationId;
        EstimatedTokens = estimatedTokens;
        HardLimitTokens = hardLimitTokens;
    }

    public Guid ConversationId { get; }

    public int EstimatedTokens { get; }

    public int HardLimitTokens { get; }
}
