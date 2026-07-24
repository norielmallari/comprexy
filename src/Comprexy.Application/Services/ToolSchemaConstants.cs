namespace Comprexy.Application.Services;

public static class ToolSchemaConstants
{
    public const string MetaToolName = "get_tool_definition";

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
                  "description": "The exact tool name from the compact tool index."
                }
              },
              "required": ["tool_name"]
            }
          }
        }
        """;
}
