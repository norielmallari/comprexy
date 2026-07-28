using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comprexy.Application.Services;

/// <summary>
/// Removes tool-calling fields from a reused OpenAI-compatible client request body.
/// Used by Inline wrap-up so the compression turn cannot continue the live tool loop.
/// </summary>
public static class ClientRequestToolStripper
{
    private static readonly string[] KeysToRemove =
    [
        "tools",
        "tool_choice",
        "functions",
        "function_call",
        "parallel_tool_calls"
    ];

    /// <summary>
    /// Returns a clone of <paramref name="request"/> with tool-related keys omitted.
    /// When nothing to strip (or non-object), returns the input unchanged.
    /// </summary>
    public static JsonElement? WithoutTools(JsonElement? request)
    {
        if (request is not { ValueKind: JsonValueKind.Object } root)
        {
            return request;
        }

        if (JsonNode.Parse(root.GetRawText()) is not JsonObject node)
        {
            return request;
        }

        var changed = false;
        foreach (var key in KeysToRemove)
        {
            if (node.Remove(key))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return request;
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
