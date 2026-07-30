using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comprexy.Application.Services;

/// <summary>
/// Shapes a reused OpenAI-compatible client request body for Inline wrap-up.
/// Keeps the live <c>tools</c>/<c>functions</c> catalog for provider KV / prompt-cache
/// alignment (many local templates render tools early in the prompt). Forces
/// <c>tool_choice</c>/<c>function_call</c> to <c>none</c> so wrap-up cannot continue
/// the agent tool loop.
/// </summary>
public static class ClientRequestToolStripper
{
    /// <summary>
    /// Clone of <paramref name="request"/> with tool-calling disabled via choice, not by
    /// removing the tools catalog. When input is null/non-object, returns it unchanged.
    /// </summary>
    public static JsonElement? ForInlineWrapUp(JsonElement? request)
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

        // Preserve tools/functions/parallel_tool_calls for wire-prefix KV with the live turn.
        if (node.ContainsKey("tools") || node.ContainsKey("tool_choice"))
        {
            if (!ToolChoiceIsNone(node["tool_choice"]))
            {
                node["tool_choice"] = "none";
                changed = true;
            }
        }

        if (node.ContainsKey("functions") || node.ContainsKey("function_call"))
        {
            if (!FunctionCallIsNone(node["function_call"]))
            {
                node["function_call"] = "none";
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

    /// <summary>
    /// Legacy helper: removes tool-related keys entirely. Prefer
    /// <see cref="ForInlineWrapUp"/> for wrap-up so tools-KV can align with live.
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
        foreach (var key in new[]
                 {
                     "tools",
                     "tool_choice",
                     "functions",
                     "function_call",
                     "parallel_tool_calls"
                 })
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

    private static bool ToolChoiceIsNone(JsonNode? toolChoice)
    {
        if (toolChoice is null)
        {
            return false;
        }

        if (toolChoice is JsonValue value &&
            value.TryGetValue<string>(out var s) &&
            string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool FunctionCallIsNone(JsonNode? functionCall)
    {
        if (functionCall is null)
        {
            return false;
        }

        if (functionCall is JsonValue value &&
            value.TryGetValue<string>(out var s) &&
            string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
