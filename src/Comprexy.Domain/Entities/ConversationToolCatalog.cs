namespace Comprexy.Domain.Entities;

/// <summary>
/// Snapshotted Virtual Tools (Tool IR) mapping for a conversation (one row per conversation in MVP).
/// </summary>
public class ConversationToolCatalog : EntityBase
{
    public Guid ConversationId { get; private set; }

    public string CatalogHash { get; private set; } = string.Empty;

    /// <summary>Validated MappingJson, or empty when <see cref="ToolIrDisabled"/>.</summary>
    public string MappingJson { get; private set; } = string.Empty;

    /// <summary>
    /// When true, schema mapping failed for this catalog hash — forward client tools unchanged
    /// (compression/budgets still run). Not full PassThrough.
    /// </summary>
    public bool ToolIrDisabled { get; private set; }

    public DateTimeOffset SnapshottedAt { get; private set; }

    private ConversationToolCatalog()
    {
    }

    public static ConversationToolCatalog Create(
        Guid conversationId,
        string catalogHash,
        string mappingJson,
        DateTimeOffset snapshottedAt,
        bool toolIrDisabled = false)
    {
        if (string.IsNullOrWhiteSpace(catalogHash))
        {
            throw new ArgumentException("Catalog hash is required.", nameof(catalogHash));
        }

        if (!toolIrDisabled && string.IsNullOrWhiteSpace(mappingJson))
        {
            throw new ArgumentException("Mapping JSON is required when Tool IR is enabled.", nameof(mappingJson));
        }

        return new ConversationToolCatalog
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            CatalogHash = catalogHash,
            MappingJson = mappingJson ?? string.Empty,
            ToolIrDisabled = toolIrDisabled,
            SnapshottedAt = snapshottedAt
        };
    }

    public void ReplaceMapping(string catalogHash, string mappingJson, DateTimeOffset snapshottedAt)
    {
        if (string.IsNullOrWhiteSpace(catalogHash))
        {
            throw new ArgumentException("Catalog hash is required.", nameof(catalogHash));
        }

        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            throw new ArgumentException("Mapping JSON is required.", nameof(mappingJson));
        }

        CatalogHash = catalogHash;
        MappingJson = mappingJson;
        ToolIrDisabled = false;
        SnapshottedAt = snapshottedAt;
    }

    public void DisableToolIr(string catalogHash, DateTimeOffset snapshottedAt)
    {
        if (string.IsNullOrWhiteSpace(catalogHash))
        {
            throw new ArgumentException("Catalog hash is required.", nameof(catalogHash));
        }

        CatalogHash = catalogHash;
        MappingJson = string.Empty;
        ToolIrDisabled = true;
        SnapshottedAt = snapshottedAt;
    }
}
