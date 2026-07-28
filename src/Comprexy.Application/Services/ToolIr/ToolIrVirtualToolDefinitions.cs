using System.Text.Json.Nodes;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>Full OpenAI tool schemas for MVP Virtual file tools.</summary>
public static class ToolIrVirtualToolDefinitions
{
    public static string GetWireJson(string toolName) => toolName switch
    {
        ToolSchemaConstants.FileManifestToolName => FileManifestWireJson,
        ToolSchemaConstants.FileRangeToolName => FileRangeWireJson,
        ToolSchemaConstants.FileSearchToolName => FileSearchWireJson,
        ToolSchemaConstants.DirListToolName => DirListWireJson,
        _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown virtual tool.")
    };

    public static JsonNode ParseWire(string toolName) =>
        JsonNode.Parse(GetWireJson(toolName))
        ?? throw new InvalidOperationException($"Failed to parse wire JSON for {toolName}.");

    public const string FileManifestWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_read_file_manifest",
            "description": "Read a compact file manifest: path, language hint, line count, size bytes, and top-level symbol/import hints when available. Does not return the file body. Prefer this over bash/stat/wc for file metadata.",
            "parameters": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Workspace-relative or absolute file path."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
          }
        }
        """;

    public const string FileRangeWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_read_file_range",
            "description": "Read a bounded line range from a file. Prefer after manifest or search. Prefer this over bash/cat/sed/head/tail for reading file contents. Output is capped; truncated=true when capped.",
            "parameters": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Workspace-relative or absolute file path."
                },
                "start_line": {
                  "type": "integer",
                  "description": "1-based inclusive start line."
                },
                "end_line": {
                  "type": "integer",
                  "description": "1-based inclusive end line."
                }
              },
              "required": ["path", "start_line", "end_line"],
              "additionalProperties": false
            }
          }
        }
        """;

    public const string FileSearchWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_read_file_search",
            "description": "Search within a path or workspace and return compact matches with path, line, and preview. Prefer over full-file reads and over bash/grep/rg for content search.",
            "parameters": {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Search query or pattern."
                },
                "path": {
                  "type": "string",
                  "description": "Optional path or directory to scope the search."
                },
                "glob": {
                  "type": "string",
                  "description": "Optional glob filter."
                }
              },
              "required": ["query"],
              "additionalProperties": false
            }
          }
        }
        """;

    public const string DirListWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_dir_list",
            "description": "Return a shallow directory listing (names and entry kinds). Prefer this over bash/ls for listing directories. Does not recurse deeply.",
            "parameters": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Directory path to list."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
          }
        }
        """;
}
