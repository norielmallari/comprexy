using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Builds the Inline follow-up wrap-up user prompt on the live chat path.
/// </summary>
public class CompressionPromptFactory
{
    private readonly string _inlineInstruction;
    private readonly string _workingMemoryTemplate;

    public CompressionPromptFactory(
        string inlineInstruction,
        string? workingMemoryTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(inlineInstruction))
        {
            throw new ArgumentException("Inline wrap-up instruction text is required.", nameof(inlineInstruction));
        }

        _workingMemoryTemplate = string.IsNullOrWhiteSpace(workingMemoryTemplate)
            ? "# Working Memory\n\n## Current Goal\n..."
            : workingMemoryTemplate.Trim();
        _inlineInstruction = inlineInstruction.Trim();
    }

    public CompressionPromptFactory(
        IOptions<CompressionOptions> options,
        IOptions<ProxyOptions> proxyOptions,
        IHostEnvironment environment)
        : this(
            LoadInstruction(options.Value.InlineInstructionFile, environment, "Prompts/compression-inline.md"),
            LoadInstruction(options.Value.WorkingMemoryTemplateFile, environment, "Prompts/working-memory-template.md"))
    {
        _ = proxyOptions;
    }

    /// <summary>
    /// Non-persisted virtual user wrap-up for eligible soft-pressure turns.
    /// Asks the live model to return only working memory (includes shared WM template).
    /// </summary>
    public ChatMessage BuildInlineWrapUpUserMessage(RulesSnapshot? rulesSnapshot = null)
    {
        var content = ComposeWithTemplate(_inlineInstruction);
        if (rulesSnapshot is { AllRules.Count: > 0 })
        {
            content += "\n\nAuthoritative consolidated rules for this fold:\n"
                + rulesSnapshot.FormatForWorkingMemory();
        }

        return new ChatMessage(MessageRole.User, content);
    }

    private string ComposeWithTemplate(string instructionBody) =>
        instructionBody.TrimEnd() + "\n\n" + _workingMemoryTemplate;

    private static string LoadInstruction(string? configuredPath, IHostEnvironment environment, string defaultRelative)
    {
        var relativePath = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultRelative
            : configuredPath.Trim();

        var path = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, relativePath));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Compression instruction file not found at '{path}'.",
                path);
        }

        return File.ReadAllText(path);
    }
}
