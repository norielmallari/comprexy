using System.Text.Json.Nodes;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>OpenAI wire schemas for Virtual IR tools (file + shell families).</summary>
public static class ToolIrVirtualToolDefinitions
{
    public static string GetWireJson(string toolName) => VirtualToolRegistry.GetWireJson(toolName);

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

    public const string ShellWireJson = """
        {
          "type": "function",
          "function": {
            "name": "comprexy_shell",
            "description": "Run a terminal command in the workspace. Prefer comprexy_read_file_range / comprexy_read_file_search / comprexy_dir_list (or client Read/Grep/Glob) over shell for file reads, search, and directory listing. Do not use for reading or editing files when a dedicated tool exists.",
            "parameters": {
              "type": "object",
              "properties": {
                "command": {
                  "type": "string",
                  "description": "The command to execute."
                },
                "working_directory": {
                  "type": "string",
                  "description": "Optional absolute working directory (defaults to workspace root)."
                },
                "block_until_ms": {
                  "type": "number",
                  "description": "Optional foreground wait in milliseconds before the client backgrounds the command. Defaults to the client tool default when omitted."
                },
                "description": {
                  "type": "string",
                  "description": "Optional short label (5-10 words) describing what the command does."
                }
              },
              "required": ["command"],
              "additionalProperties": false
            }
          }
        }
        """;
}
