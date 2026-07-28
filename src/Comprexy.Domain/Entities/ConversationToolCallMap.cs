namespace Comprexy.Domain.Entities;

/// <summary>
/// Durable IR↔client tool_call_id mapping for an open Virtual Tools round.
/// SQLite is source of truth across process restart; in-memory map is a hot cache.
/// </summary>
public class ConversationToolCallMap : EntityBase
{
    public Guid ConversationId { get; private set; }

    public string IrCallId { get; private set; } = string.Empty;

    public string ClientCallId { get; private set; } = string.Empty;

    public string ComprexyToolName { get; private set; } = string.Empty;

    public string? ClientToolName { get; private set; }

    public string IrArgumentsJson { get; private set; } = "{}";

    public string? ClientArgumentsJson { get; private set; }

    public string Strategy { get; private set; } = string.Empty;

    public string? Path { get; private set; }

    public int? StartLine { get; private set; }

    public int? EndLine { get; private set; }

    /// <summary>True while awaiting an inbound client tool result. MVP deletes the row on complete.</summary>
    public bool Pending { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private ConversationToolCallMap()
    {
    }

    public static ConversationToolCallMap CreatePending(
        Guid conversationId,
        string irCallId,
        string clientCallId,
        string comprexyToolName,
        string? clientToolName,
        string irArgumentsJson,
        string? clientArgumentsJson,
        string strategy,
        string? path,
        int? startLine,
        int? endLine,
        DateTimeOffset registeredAt)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id is required.", nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(irCallId))
        {
            throw new ArgumentException("IR call id is required.", nameof(irCallId));
        }

        if (string.IsNullOrWhiteSpace(clientCallId))
        {
            throw new ArgumentException("Client call id is required.", nameof(clientCallId));
        }

        if (string.IsNullOrWhiteSpace(comprexyToolName))
        {
            throw new ArgumentException("Comprexy tool name is required.", nameof(comprexyToolName));
        }

        if (string.IsNullOrWhiteSpace(strategy))
        {
            throw new ArgumentException("Strategy is required.", nameof(strategy));
        }

        return new ConversationToolCallMap
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            IrCallId = irCallId,
            ClientCallId = clientCallId,
            ComprexyToolName = comprexyToolName,
            ClientToolName = clientToolName,
            IrArgumentsJson = string.IsNullOrWhiteSpace(irArgumentsJson) ? "{}" : irArgumentsJson,
            ClientArgumentsJson = clientArgumentsJson,
            Strategy = strategy,
            Path = path,
            StartLine = startLine,
            EndLine = endLine,
            Pending = true,
            RegisteredAt = registeredAt,
            CompletedAt = null
        };
    }
}
