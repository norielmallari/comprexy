namespace Comprexy.Domain.Entities;

/// <summary>
/// Full client tool definition snapshot for passthrough and argument shapes.
/// </summary>
public class ConversationToolDefinition : EntityBase
{
    public Guid ConversationId { get; private set; }

    public string ToolName { get; private set; } = string.Empty;

    public string DefinitionHash { get; private set; } = string.Empty;

    public string DefinitionJson { get; private set; } = string.Empty;

    private ConversationToolDefinition()
    {
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
            DefinitionJson = definitionJson
        };
    }

    public void ReplaceSnapshot(string definitionHash, string definitionJson)
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
    }
}
