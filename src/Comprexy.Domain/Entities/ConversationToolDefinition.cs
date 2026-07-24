namespace Comprexy.Domain.Entities;

/// <summary>
/// Full tool definition persisted after hydration via the meta-tool.
/// </summary>
public class ConversationToolDefinition : EntityBase
{
    public Guid ConversationId { get; private set; }

    public string ToolName { get; private set; } = string.Empty;

    public string DefinitionHash { get; private set; } = string.Empty;

    public string DefinitionJson { get; private set; } = string.Empty;

    public DateTimeOffset? HydratedAt { get; private set; }

    private ConversationToolDefinition()
    {
    }

    public static ConversationToolDefinition Create(
        Guid conversationId,
        string toolName,
        string definitionHash,
        string definitionJson,
        DateTimeOffset hydratedAt)
    {
        var entity = CreateFromSnapshot(conversationId, toolName, definitionHash, definitionJson);
        entity.HydratedAt = hydratedAt;
        return entity;
    }

    public static ConversationToolDefinition CreateFromSnapshot(
        Guid conversationId,
        string toolName,
        string definitionHash,
        string definitionJson)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }

        if (string.IsNullOrWhiteSpace(definitionHash))
        {
            throw new ArgumentException("Definition hash is required.", nameof(definitionHash));
        }

        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            throw new ArgumentException("Definition JSON is required.", nameof(definitionJson));
        }

        return new ConversationToolDefinition
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            ToolName = toolName,
            DefinitionHash = definitionHash,
            DefinitionJson = definitionJson,
            HydratedAt = null
        };
    }

    public bool IsHydrated => HydratedAt.HasValue;

    public void MarkHydrated(string definitionHash, string definitionJson, DateTimeOffset hydratedAt)
    {
        if (string.IsNullOrWhiteSpace(definitionHash))
        {
            throw new ArgumentException("Definition hash is required.", nameof(definitionHash));
        }

        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            throw new ArgumentException("Definition JSON is required.", nameof(definitionJson));
        }

        DefinitionHash = definitionHash;
        DefinitionJson = definitionJson;
        HydratedAt = hydratedAt;
    }
}
