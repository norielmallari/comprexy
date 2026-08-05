using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Comprexy.Bench.Tools;

/// <summary>
/// Default MAF bench client tool catalog: real sandbox backends plus IDE-weight denylist and
/// Task stubs. Both arms share this catalog; arm difference is proxy <c>ToolSchema:Mode</c> only.
/// </summary>
internal static class SandboxToolCatalog
{
    /// <summary>
    /// Provenance stamp for the default catalog shape. Bump whenever default tool schemas change.
    /// </summary>
    public const string CatalogVersion = "ide-band-v1";

    /// <summary>
    /// Stock <c>ToolSchema:ExcludeFromModelTools</c> names the denylist stubs must match exactly.
    /// </summary>
    public static readonly IReadOnlyList<string> StockExcludeFromModelTools =
    [
        "ReadLints",
        "TodoWrite",
        "AwaitShell",
        "UpdateCurrentStep",
        "EditNotebook",
        "SwitchMode",
        "agent_manager",
        "agent_manager_models",
        "background_process",
        "kilo_local_recall"
    ];

    public static IList<AITool> CreateTools(SandboxWorkspace workspace, TimeSpan shellTimeout)
    {
        var backends = new SandboxTools(workspace, shellTimeout).CreateBackendTools();
        var catalog = new List<AITool>(backends.Count + StockExcludeFromModelTools.Count + 1);
        catalog.AddRange(backends);
        catalog.AddRange(SandboxDenylistStubs.CreateTools());
        catalog.Add(SandboxTaskStub.Create());
        return catalog;
    }

    /// <summary>
    /// Compact OpenAI chat-completions <c>tools[]</c> JSON array
    /// (<c>type</c>/<c>function</c>/<c>name</c>/<c>description</c>/<c>parameters</c>) for cl100k
    /// band checks and optional manifest stamping.
    /// </summary>
    public static string ToCompactOpenAiToolsJson(IList<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var tool in tools)
            {
                if (tool is not AIFunctionDeclaration declaration)
                {
                    throw new InvalidOperationException(
                        $"Catalog tool '{tool.Name}' is not an AIFunctionDeclaration; cannot emit OpenAI tools JSON.");
                }

                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                writer.WriteString("name", declaration.Name);
                writer.WriteString("description", declaration.Description ?? string.Empty);
                writer.WritePropertyName("parameters");
                declaration.JsonSchema.WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
