using Comprexy.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Loads tool-schema system rules from disk (same pattern as <see cref="CompressionPromptFactory"/>).
/// </summary>
public class ToolSchemaPromptFactory
{
    private readonly string _instruction;

    public ToolSchemaPromptFactory(IOptions<ToolSchemaOptions> options, IHostEnvironment environment)
        : this(LoadInstruction(options.Value.InstructionFile, environment))
    {
    }

    public ToolSchemaPromptFactory(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("Tool schema instruction text is required.", nameof(instruction));
        }

        _instruction = instruction.Trim();
    }

    public string BuildSystemContent(string compactIndexJson)
    {
        if (string.IsNullOrWhiteSpace(compactIndexJson))
        {
            throw new ArgumentException("Compact index JSON is required.", nameof(compactIndexJson));
        }

        return $"{_instruction.TrimEnd()}\n\n## Compact tool index\n\n```json\n{compactIndexJson.Trim()}\n```";
    }

    private static string LoadInstruction(string? configuredPath, IHostEnvironment environment)
    {
        var relativePath = string.IsNullOrWhiteSpace(configuredPath)
            ? "Prompts/tool-schema.md"
            : configuredPath.Trim();

        var path = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, relativePath));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Tool schema instruction file not found at '{path}'.", path);
        }

        return File.ReadAllText(path);
    }
}
