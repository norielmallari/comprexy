using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comprexy.Application.Services;

/// <summary>
/// Removes reasoning / chain-of-thought fields from OpenAI-compatible message objects
/// before they are sent upstream. Persisted wire JSON is left unchanged.
/// </summary>
public static class ReasoningContentStripper
{
    private static readonly HashSet<string> ReasoningFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "reasoning_content",
        "reasoning"
    };

    public static bool IsReasoningField(string name) => ReasoningFieldNames.Contains(name);

    /// <summary>
    /// Returns wire JSON with reasoning fields removed, or the original string when disabled /
    /// not an object / unparseable.
    /// </summary>
    public static string StripFromWireJson(string rawWireJson, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(rawWireJson))
        {
            return rawWireJson;
        }

        try
        {
            var node = JsonNode.Parse(rawWireJson);
            if (node is not JsonObject obj)
            {
                return rawWireJson;
            }

            StripFromMessageObject(obj);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return rawWireJson;
        }
    }

    /// <summary>
    /// Mutates a <c>messages</c> JSON array in place, stripping reasoning fields from each object.
    /// </summary>
    public static void StripFromMessagesArray(JsonNode? messagesNode, bool enabled)
    {
        if (!enabled || messagesNode is not JsonArray messages)
        {
            return;
        }

        foreach (var item in messages)
        {
            if (item is JsonObject message)
            {
                StripFromMessageObject(message);
            }
        }
    }

    public static void StripFromMessageObject(JsonObject message)
    {
        foreach (var name in message.Select(p => p.Key).Where(IsReasoningField).ToList())
        {
            message.Remove(name);
        }
    }
}
