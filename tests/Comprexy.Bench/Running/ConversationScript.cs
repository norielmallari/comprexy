using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Comprexy.Bench.Running;

/// <summary>
/// A frozen prompt list. Both arms replay the same file, and the manifest records
/// <see cref="PromptListHash"/> so a report can refuse to pair conversations whose prompts drifted.
/// </summary>
internal sealed record ConversationScript(
    string Name,
    string SystemPrompt,
    IReadOnlyList<string> Prompts,
    string PromptListHash)
{
    public static IReadOnlyList<ConversationScript> LoadAll(IReadOnlyList<string> filter)
    {
        var directory = BenchPaths.ConversationsDirectory;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Conversation scripts not found: {directory}");
        }

        var scripts = Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(Load)
            .ToList();

        if (filter.Count == 0)
        {
            return scripts;
        }

        var selected = scripts.Where(s => filter.Contains(s.Name, StringComparer.Ordinal)).ToList();
        var missing = filter.Except(selected.Select(s => s.Name), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                $"No conversation script named: {string.Join(", ", missing)}");
        }

        return selected;
    }

    private static ConversationScript Load(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);

        var (systemPrompt, prompts) = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => (
                (string?)null,
                document.RootElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()),
            JsonValueKind.Object => ReadObject(name, document.RootElement),
            _ => throw new InvalidOperationException(
                $"Conversation script {name} must be a JSON array of prompts or a script object.")
        };

        if (prompts.Count == 0)
        {
            throw new InvalidOperationException($"Conversation script {name} has no prompts.");
        }

        var composedSystemPrompt = BenchSystemPrompt.Compose(systemPrompt);

        return new ConversationScript(
            name, composedSystemPrompt, prompts, HashPrompts(composedSystemPrompt, prompts));
    }

    private static (string?, List<string>) ReadObject(string name, JsonElement root)
    {
        var systemPrompt = root.TryGetProperty("systemPrompt", out var system) ? system.GetString() : null;

        if (root.TryGetProperty("workspaceSeed", out _))
        {
            throw new InvalidOperationException(
                $"Conversation script {name} still sets 'workspaceSeed'. Seeded fixture directories are gone: " +
                "every conversation now runs against a throwaway git clone of this repository.");
        }

        if (!root.TryGetProperty("prompts", out var promptsElement) ||
            promptsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("A conversation script object requires a 'prompts' array.");
        }

        return (
            systemPrompt,
            promptsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList());
    }

    private static string HashPrompts(string systemPrompt, IReadOnlyList<string> prompts)
    {
        var builder = new StringBuilder();
        builder.Append(systemPrompt).Append('\u001f');
        foreach (var prompt in prompts)
        {
            builder.Append(prompt).Append('\u001f');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
