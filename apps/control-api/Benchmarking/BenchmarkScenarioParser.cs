using System.Text.Json;
using Comprexy.ControlApi.Contracts.Benchmark;

namespace Comprexy.ControlApi.Benchmarking;

public static class BenchmarkScenarioParser
{
    public static BenchmarkScenarioDto Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        return new BenchmarkScenarioDto
        {
            Name = name,
            PromptCount = CountPrompts(root),
            Description = ReadOptionalString(root, "description") ?? ReadOptionalString(root, "provenance"),
            IsSmoke = name.StartsWith("smoke-", StringComparison.Ordinal),
        };
    }

    public static bool IsSmokeScenario(string scenarioName) =>
        scenarioName.StartsWith("smoke-", StringComparison.Ordinal);

    public static bool IsSmokeOnlyRun(IReadOnlyList<string> conversations) =>
        conversations.Count > 0 && conversations.All(IsSmokeScenario);

    private static int CountPrompts(JsonElement root)
    {
        return root.ValueKind switch
        {
            JsonValueKind.Array => root.GetArrayLength(),
            JsonValueKind.Object when root.TryGetProperty("largestFiles", out var largestFiles)
                => largestFiles.TryGetProperty("count", out var count) ? count.GetInt32() : 10,
            JsonValueKind.Object when root.TryGetProperty("prompts", out var prompts)
                && prompts.ValueKind == JsonValueKind.Array => prompts.GetArrayLength(),
            _ => 0,
        };
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
