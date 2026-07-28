namespace Comprexy.Application.Services;

public static class ToolSchemaConstants
{
    public const string ConversationIdMetaToolName = "comprexy_get_current_conversation_id";

    public const string FileManifestToolName = "comprexy_read_file_manifest";
    public const string FileRangeToolName = "comprexy_read_file_range";
    public const string FileSearchToolName = "comprexy_read_file_search";
    public const string DirListToolName = "comprexy_dir_list";

    public static readonly IReadOnlyList<string> VirtualFileToolNames =
    [
        FileManifestToolName,
        FileRangeToolName,
        FileSearchToolName,
        DirListToolName
    ];

    public const string ConversationIdMetaToolWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_get_current_conversation_id",
            "description": "Return the ConversationId UUID for this chat session. Call before any Comprexy telemetry MCP tool that requires conversationId. No arguments.",
            "parameters": {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
          }
        }
        """;

    public static bool IsConversationIdMetaTool(string? name) =>
        string.Equals(name, ConversationIdMetaToolName, StringComparison.Ordinal);

    public static bool IsVirtualFileTool(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        VirtualFileToolNames.Contains(name, StringComparer.Ordinal);

    public static bool IsReservedToolName(string? name) =>
        IsConversationIdMetaTool(name) || IsVirtualFileTool(name);
}
