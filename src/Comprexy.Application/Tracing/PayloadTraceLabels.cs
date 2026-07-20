namespace Comprexy.Application.Tracing;

/// <summary>
/// Canonical payload-trace labels for client-facing and model-facing hops.
/// Wire labels reflect bytes on the HTTP connection; reassembled labels are reconstructed
/// by Comprexy (e.g. streaming chunks folded into a single completion for persistence).
/// Each label maps to a <see cref="PayloadTraceCategory"/> so it can be independently toggled.
/// </summary>
public static class PayloadTraceLabels
{
    public const string ClientInput = "client input";
    public const string ClientOutput = "client output";
    public const string ClientOutputReassembled = "client output (reassembled)";
    public const string ModelInput = "model input";
    public const string ModelOutput = "model output";
    public const string ModelOutputReassembled = "model output (reassembled)";
    public const string CompressionModelInput = "compression model input";
    public const string CompressionModelOutput = "compression model output";
    public const string CompressionModelOutputReassembled = "compression model output (reassembled)";
    public const string ContextBudgetReassembled = "context budget (reassembled)";
    public const string ContextBudgetPostEmergency = "context budget (reassembled, post-emergency)";
    public const string ContextBudgetPassThrough = "context budget (pass-through)";

    /// <summary>Resolves the toggleable category for a given trace label.</summary>
    public static PayloadTraceCategory ResolveCategory(string label) => label switch
    {
        ClientInput => PayloadTraceCategory.ClientInput,
        ClientOutput or ClientOutputReassembled => PayloadTraceCategory.ClientOutput,
        ModelInput => PayloadTraceCategory.ModelInput,
        ModelOutput or ModelOutputReassembled => PayloadTraceCategory.ModelOutput,
        CompressionModelInput => PayloadTraceCategory.CompressionModelInput,
        CompressionModelOutput or CompressionModelOutputReassembled => PayloadTraceCategory.CompressionModelOutput,
        ContextBudgetReassembled or ContextBudgetPostEmergency or ContextBudgetPassThrough => PayloadTraceCategory.ContextBudget,
        _ => PayloadTraceCategory.ClientOutput
    };
}
