namespace Comprexy.Application.Models;

/// <summary>
/// Pass-through sampling parameters for a single upstream chat completion call.
/// </summary>
public sealed record ChatCompletionCallOptions(
    double? Temperature = null,
    double? TopP = null,
    int? MaxTokens = null,
    IReadOnlyList<string>? Stop = null);
