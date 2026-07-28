namespace Comprexy.Application.Models.Retrieval;

public sealed class ConversationSearchResultDto
{
    public Guid ConversationId { get; init; }

    public string Query { get; init; } = string.Empty;

    public IReadOnlyList<ConversationSearchMatchDto> Matches { get; init; } = [];
}

public sealed class ConversationSearchMatchDto
{
    /// <summary><c>message</c> or <c>working_memory</c>.</summary>
    public string SourceType { get; init; } = string.Empty;

    public int? Sequence { get; init; }

    public string? Role { get; init; }

    public int? WorkingMemoryVersion { get; init; }

    public bool? IsFolded { get; init; }

    public string Text { get; init; } = string.Empty;
}

public sealed class ConversationMessageSnippetDto
{
    public int Sequence { get; init; }

    public string Role { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public int TokenCount { get; init; }

    public bool IsFolded { get; init; }

    public int? FoldedIntoWorkingMemoryVersion { get; init; }

    public string? RawWireJson { get; init; }
}

public sealed class WorkingMemorySnapshotDto
{
    public Guid ConversationId { get; init; }

    public int Version { get; init; }

    public string Content { get; init; } = string.Empty;

    public int TokenCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class OpenToolChainsDto
{
    public Guid ConversationId { get; init; }

    /// <summary>
    /// True when any announced assistant tool_call id lacks a matching tool result
    /// (same closed-chain rule as compression).
    /// </summary>
    public bool IsOpen { get; init; }

    /// <summary>
    /// True when <see cref="IsOpen"/> solely because the tip assistant's tool_calls have no
    /// results yet (typical while the client is still executing a parallel tool batch).
    /// False for stuck/partial opens that span older turns.
    /// </summary>
    public bool IsAwaitingClientToolResults { get; init; }

    public int UnmatchedCount { get; init; }

    public IReadOnlyList<string> OpenToolCallIds { get; init; } = [];
}
