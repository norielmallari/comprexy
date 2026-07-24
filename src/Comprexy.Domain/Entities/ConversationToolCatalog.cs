namespace Comprexy.Domain.Entities;

/// <summary>
/// Snapshotted compact tool index for a conversation (one row per conversation in MVP).
/// </summary>
public class ConversationToolCatalog : EntityBase
{
    public Guid ConversationId { get; private set; }

    public string CatalogHash { get; private set; } = string.Empty;

    public string CompactIndexJson { get; private set; } = string.Empty;

    public DateTimeOffset SnapshottedAt { get; private set; }

    private ConversationToolCatalog()
    {
    }

    public static ConversationToolCatalog Create(
        Guid conversationId,
        string catalogHash,
        string compactIndexJson,
        DateTimeOffset snapshottedAt)
    {
        if (string.IsNullOrWhiteSpace(catalogHash))
        {
            throw new ArgumentException("Catalog hash is required.", nameof(catalogHash));
        }

        if (string.IsNullOrWhiteSpace(compactIndexJson))
        {
            throw new ArgumentException("Compact index JSON is required.", nameof(compactIndexJson));
        }

        return new ConversationToolCatalog
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            CatalogHash = catalogHash,
            CompactIndexJson = compactIndexJson,
            SnapshottedAt = snapshottedAt
        };
    }
}
