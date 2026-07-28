namespace Comprexy.Application.Services;

public static class ToolSchemaConstants
{
    public const string MetaToolName = "get_tool_definition";

    public const string ConversationIdMetaToolName = "get_current_conversation_id";

    public const string MetaToolWireJson = """
        {
          "type": "function",
          "function": {
            "name": "get_tool_definition",
            "description": "Get the full JSON schema and validation rules for a tool from the compact index.",
            "parameters": {
              "type": "object",
              "properties": {
                "tool_name": {
                  "type": "string",
                  "description": "The exact tool name from the compact index."
                }
              },
              "required": ["tool_name"]
            }
          }
        }
        """;

    public const string ConversationIdMetaToolWireJson = """
        {
          "type": "function",
          "function": {
            "name": "get_current_conversation_id",
            "description": "Return the ConversationId UUID for this chat session. Call before any tool that requires conversationId (for example comprexy telemetry MCP tools). No arguments.",
            "parameters": {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
          }
        }
        """;

    public static bool IsReservedMetaToolName(string? name) =>
        string.Equals(name, MetaToolName, StringComparison.Ordinal) ||
        string.Equals(name, ConversationIdMetaToolName, StringComparison.Ordinal);
}
