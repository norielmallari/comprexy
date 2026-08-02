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
    /// <summary>Placeholder in a prompt template replaced with a frozen fixtures file body.</summary>
    public const string FixturePlaceholder = "{{fixture}}";

    private static readonly string[] DefaultExcludeDirectoryNames =
    [
        ".git",
        ".next",
        ".cursor",
        "bin",
        "data",
        "fixtures",
        "node_modules",
        "obj",
        "reports",
    ];

    private static readonly string[] DefaultExtensions = [".cs", ".md", ".ts", ".tsx"];

    private static readonly string[] DefaultExcludeFileNames = ["package-lock.json"];

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

        string? fixtureFileName = null;
        var (systemPrompt, prompts) = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => (
                (string?)null,
                document.RootElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()),
            JsonValueKind.Object => ReadObject(name, document.RootElement, out fixtureFileName),
            _ => throw new InvalidOperationException(
                $"Conversation script {name} must be a JSON array of prompts or a script object.")
        };

        if (!string.IsNullOrWhiteSpace(fixtureFileName))
        {
            prompts = ApplyFixture(name, fixtureFileName, prompts);
        }

        if (prompts.Count == 0)
        {
            throw new InvalidOperationException($"Conversation script {name} has no prompts.");
        }

        var composedSystemPrompt = BenchSystemPrompt.Compose(systemPrompt);

        return new ConversationScript(
            name, composedSystemPrompt, prompts, HashPrompts(composedSystemPrompt, prompts));
    }

    private static (string?, List<string>) ReadObject(string name, JsonElement root, out string? fixtureFileName)
    {
        var systemPrompt = root.TryGetProperty("systemPrompt", out var system) ? system.GetString() : null;
        fixtureFileName = root.TryGetProperty("fixture", out var fixture) ? fixture.GetString() : null;
        if (string.IsNullOrWhiteSpace(fixtureFileName) &&
            root.TryGetProperty("injectFixture", out var injectFixture))
        {
            fixtureFileName = injectFixture.GetString();
        }

        if (root.TryGetProperty("workspaceSeed", out _))
        {
            throw new InvalidOperationException(
                $"Conversation script {name} still sets 'workspaceSeed'. Seeded fixture directories are gone: " +
                "every conversation now runs against a throwaway git clone of this repository.");
        }

        if (root.TryGetProperty("largestFiles", out var largestFilesElement))
        {
            var options = ParseLargestFilesOptions(largestFilesElement);
            var template = root.TryGetProperty("promptTemplate", out var templateElement)
                ? templateElement.GetString()
                : null;
            return (systemPrompt, LargestFilesPromptBuilder.Build(name, options, template ?? string.Empty));
        }

        if (!root.TryGetProperty("prompts", out var promptsElement) ||
            promptsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "A conversation script object requires a 'prompts' array or a 'largestFiles' block.");
        }

        return (
            systemPrompt,
            promptsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList());
    }

    private static RepoLargestFilesSelector.Options ParseLargestFilesOptions(JsonElement element)
    {
        var count = element.TryGetProperty("count", out var countElement)
            ? countElement.GetInt32()
            : 10;

        return new RepoLargestFilesSelector.Options(
            count,
            ReadStringArray(element, "excludeDirectoryNames", DefaultExcludeDirectoryNames),
            ReadStringArray(element, "extensions", DefaultExtensions),
            ReadStringArray(element, "excludeFileNames", DefaultExcludeFileNames));
    }

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement parent,
        string propertyName,
        IReadOnlyList<string> defaults)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return defaults;
        }

        return array.EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static List<string> ApplyFixture(string name, string fixtureFileName, List<string> prompts)
    {
        var fixturePath = Path.Combine(BenchPaths.ConversationsDirectory, "fixtures", fixtureFileName);
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Conversation script {name} references fixture '{fixtureFileName}' but the file was not found: {fixturePath}",
                fixturePath);
        }

        var fixtureBody = File.ReadAllText(fixturePath);
        var applied = new List<string>(prompts.Count);
        foreach (var prompt in prompts)
        {
            if (!prompt.Contains(FixturePlaceholder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Conversation script {name} sets 'fixture' but a prompt is missing the {FixturePlaceholder} placeholder.");
            }

            applied.Add(prompt.Replace(FixturePlaceholder, fixtureBody, StringComparison.Ordinal));
        }

        return applied;
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
