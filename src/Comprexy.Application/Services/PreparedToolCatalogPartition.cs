using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;

namespace Comprexy.Application.Services;

/// <summary>
/// Shared membership for model-facing tool catalogs: Virtual IR + meta vs client passthrough.
/// Rewrite and prepare metrics must use the same partition so segments cannot drift.
/// </summary>
public static class PreparedToolCatalogPartition
{
    /// <summary>
    /// Builds ordered wire-JSON definition strings matching
    /// <c>ToolSchemaOrchestrator.BuildRewrittenClientRequest</c> membership.
    /// </summary>
    public static (
        IReadOnlyList<string> VirtualAndMetaWireJson,
        IReadOnlyList<string> ClientPassthroughWireJson)
        BuildModelFacingToolDefinitions(ToolSchemaSession session, ToolSchemaOptions options)
    {
        var virtualAndMeta = new List<string>();
        foreach (var name in VirtualToolRegistry.VirtualToolNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (session.BoundVirtualToolNames.Contains(name))
            {
                virtualAndMeta.Add(ToolIrVirtualToolDefinitions.BuildWireJson(name, options));
            }
        }

        virtualAndMeta.Add(ToolSchemaConstants.ConversationIdMetaToolWireJson);

        var client = new List<string>();
        foreach (var (toolName, definitionJson) in session.FullDefinitionsByName
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (session.IsHiddenFromModelClientTool(toolName))
            {
                continue;
            }

            if (ToolSchemaConstants.IsReservedToolName(toolName))
            {
                continue;
            }

            client.Add(definitionJson);
        }

        return (virtualAndMeta, client);
    }

    /// <summary>
    /// Sums tiktoken over each partition's wire-JSON strings (no array framing).
    /// </summary>
    public static (int Virtual, int Client) EstimateFromSession(
        ITokenEstimator estimator,
        ToolSchemaSession session,
        ToolSchemaOptions options)
    {
        var (virtualAndMeta, client) = BuildModelFacingToolDefinitions(session, options);
        var virtualTokens = 0;
        foreach (var wire in virtualAndMeta)
        {
            virtualTokens += estimator.CountTokens(wire);
        }

        var clientTokens = 0;
        foreach (var wire in client)
        {
            clientTokens += estimator.CountTokens(wire);
        }

        return (virtualTokens, clientTokens);
    }

    /// <summary>
    /// Client catalog segment when VT rewrite did not produce a session: tools/functions only.
    /// </summary>
    public static int EstimateClientFromRequestRoot(
        ITokenEstimator estimator,
        JsonElement? requestRoot) =>
        estimator.CountPromptSideToolsTokens(requestRoot);
}
