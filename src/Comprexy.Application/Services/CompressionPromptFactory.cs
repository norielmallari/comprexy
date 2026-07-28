using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Builds prompts for Fixed compression (system + labeled transcript), Smart trailing
/// user instruction (live chat prefix is assembled separately via <see cref="ContextBuilder"/>),
/// and Inline follow-up wrap-up on the live chat path.
/// </summary>
public class CompressionPromptFactory
{
    private readonly string _fixedInstruction;
    private readonly string _smartInstruction;
    private readonly string _inlineInstruction;
    private readonly string _workingMemoryTemplate;
    private readonly bool _stripReasoningContent;

    public CompressionPromptFactory(
        string fixedInstruction,
        string? smartInstruction = null,
        string? inlineInstruction = null,
        string? workingMemoryTemplate = null,
        bool stripReasoningContent = false)
    {
        if (string.IsNullOrWhiteSpace(fixedInstruction))
        {
            throw new ArgumentException("Compression instruction text is required.", nameof(fixedInstruction));
        }

        _workingMemoryTemplate = string.IsNullOrWhiteSpace(workingMemoryTemplate)
            ? "# Working Memory\n\n## Current Goal\n..."
            : workingMemoryTemplate.Trim();
        _fixedInstruction = fixedInstruction.Trim();
        _smartInstruction = string.IsNullOrWhiteSpace(smartInstruction)
            ? _fixedInstruction
            : smartInstruction.Trim();
        _inlineInstruction = string.IsNullOrWhiteSpace(inlineInstruction)
            ? _fixedInstruction
            : inlineInstruction.Trim();
        _stripReasoningContent = stripReasoningContent;
    }

    public CompressionPromptFactory(
        IOptions<CompressionOptions> options,
        IOptions<ProxyOptions> proxyOptions,
        IHostEnvironment environment)
        : this(
            LoadInstruction(options.Value.InstructionFile, environment, "Prompts/compression-fixed.md"),
            LoadInstruction(options.Value.SmartInstructionFile, environment, "Prompts/compression-smart.md"),
            LoadInstruction(options.Value.InlineInstructionFile, environment, "Prompts/compression-inline.md"),
            LoadInstruction(options.Value.WorkingMemoryTemplateFile, environment, "Prompts/working-memory-template.md"),
            proxyOptions.Value.StripReasoningContent)
    {
    }

    /// <summary>
    /// Non-persisted virtual user wrap-up for eligible Inline soft-pressure turns.
    /// Asks the live model to return only working memory (includes shared WM template).
    /// </summary>
    public ChatMessage BuildInlineWrapUpUserMessage()
    {
        var content = ComposeWithTemplate(_inlineInstruction);
        return new ChatMessage(MessageRole.User, content);
    }

    /// <summary>
    /// Incremental merge: existing working memory plus a raw segment to fold in.
    /// </summary>
    public IReadOnlyList<ChatMessage> BuildMessages(
        WorkingMemory? existingWorkingMemory,
        IReadOnlyList<ConversationMessage> messagesToFold)
    {
        var transcript = new StringBuilder();
        transcript.Append("## Existing Working Memory\n");
        transcript.Append(existingWorkingMemory is null ? "None yet." : existingWorkingMemory.Content);
        transcript.Append("\n\n## Conversation Segment To Fold In\n");
        transcript.Append(
            "Messages may include tool calls and tool results (for example file reads). ");
        transcript.Append(
            "Preserve durable facts from those tools in working memory; do not drop file reads as irrelevant.\n\n");

        foreach (var message in messagesToFold)
        {
            transcript.Append(FormatMessage(message)).Append('\n');
        }

        return CreateFixedPrompt(transcript.ToString());
    }

    /// <summary>
    /// Full rebuild: produce a fresh working memory from the full raw transcript only
    /// (no prior working memory as input).
    /// </summary>
    public IReadOnlyList<ChatMessage> BuildMessagesFromFullRaw(IReadOnlyList<ConversationMessage> allMessages)
    {
        var transcript = new StringBuilder();
        transcript.Append("Produce a fresh working memory from this transcript only.\n\n");
        transcript.Append("## Full Conversation Transcript\n");
        transcript.Append(
            "Messages may include tool calls and tool results (for example file reads). ");
        transcript.Append(
            "Preserve durable facts from those tools in working memory; do not drop file reads as irrelevant.\n\n");

        foreach (var message in allMessages.OrderBy(m => m.Sequence))
        {
            transcript.Append(FormatMessage(message)).Append('\n');
        }

        return CreateFixedPrompt(transcript.ToString());
    }

    /// <summary>
    /// Trailing user turn for Smart compression: instruction text plus the retain index.
    /// </summary>
    public ChatMessage BuildSmartInstructionMessage(string retainIndex)
    {
        if (string.IsNullOrWhiteSpace(retainIndex))
        {
            throw new ArgumentException("Retain index is required.", nameof(retainIndex));
        }

        var content = ComposeWithTemplate(_smartInstruction).TrimEnd() + "\n\n" + retainIndex.Trim();
        return new ChatMessage(MessageRole.User, content);
    }

    private string ComposeWithTemplate(string instructionBody) =>
        instructionBody.TrimEnd() + "\n\n" + _workingMemoryTemplate;

    private IReadOnlyList<ChatMessage> CreateFixedPrompt(string userContent) =>
        new List<ChatMessage>
        {
            new(MessageRole.System, ComposeWithTemplate(_fixedInstruction)),
            new(MessageRole.User, userContent)
        };

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

    private string FormatMessage(ConversationMessage message)
    {
        var body = !string.IsNullOrWhiteSpace(message.RawWireJson)
            ? $"{message.Role} (wire): {NormalizeWireJson(message.RawWireJson)}"
            : $"{message.Role}: {message.Content}";

        return body;
    }

    private string NormalizeWireJson(string rawWireJson)
    {
        try
        {
            var node = JsonNode.Parse(rawWireJson);
            if (node is not JsonObject message)
            {
                return rawWireJson;
            }

            if (_stripReasoningContent)
            {
                ReasoningContentStripper.StripFromMessageObject(message);
            }

            SimplifyToolCallsForCompression(message);
            RedactImageDataUrls(message);
            return message.ToJsonString();
        }
        catch (JsonException)
        {
            return rawWireJson;
        }
    }

    /// <summary>
    /// Replace inline <c>data:image/...;base64,...</c> payloads with a short placeholder so the
    /// compression model does not ingest megabytes of binary as text.
    /// </summary>
    private static void RedactImageDataUrls(JsonObject message)
    {
        if (!message.TryGetPropertyValue("content", out var contentNode) || contentNode is not JsonArray parts)
        {
            return;
        }

        foreach (var item in parts)
        {
            if (item is not JsonObject part)
            {
                continue;
            }

            if (!part.TryGetPropertyValue("type", out var typeNode) ||
                typeNode is not JsonValue typeValue ||
                !typeValue.TryGetValue<string>(out var type) ||
                !string.Equals(type, "image_url", StringComparison.Ordinal))
            {
                continue;
            }

            if (!part.TryGetPropertyValue("image_url", out var imageUrlNode))
            {
                continue;
            }

            if (imageUrlNode is JsonValue imageUrlString &&
                imageUrlString.TryGetValue<string>(out var urlString))
            {
                part["image_url"] = VisionImageTokenEstimator.RedactImageUrlForText(urlString);
                continue;
            }

            if (imageUrlNode is not JsonObject imageUrlObject)
            {
                continue;
            }

            if (imageUrlObject.TryGetPropertyValue("url", out var urlNode) &&
                urlNode is JsonValue urlValue &&
                urlValue.TryGetValue<string>(out var url))
            {
                imageUrlObject["url"] = VisionImageTokenEstimator.RedactImageUrlForText(url);
            }
        }
    }

    /// <summary>
    /// Collapses OpenAI tool_call wire shape for the compressor:
    /// drops redundant <c>"type":"function"</c> and flattens
    /// <c>function:{name,arguments}</c> to top-level name/arguments.
    /// </summary>
    private static void SimplifyToolCallsForCompression(JsonObject message)
    {
        if (!message.TryGetPropertyValue("tool_calls", out var toolCallsNode) ||
            toolCallsNode is not JsonArray toolCalls)
        {
            return;
        }

        foreach (var item in toolCalls)
        {
            if (item is not JsonObject toolCall)
            {
                continue;
            }

            if (toolCall.TryGetPropertyValue("type", out var typeNode) &&
                typeNode is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var type) &&
                string.Equals(type, "function", StringComparison.OrdinalIgnoreCase))
            {
                toolCall.Remove("type");
            }

            if (!toolCall.TryGetPropertyValue("function", out var functionNode) ||
                functionNode is not JsonObject function)
            {
                continue;
            }

            if (function.TryGetPropertyValue("name", out var name) &&
                !toolCall.ContainsKey("name"))
            {
                toolCall["name"] = name?.DeepClone();
            }

            if (function.TryGetPropertyValue("arguments", out var arguments) &&
                !toolCall.ContainsKey("arguments"))
            {
                toolCall["arguments"] = ExpandArgumentsIfJson(arguments);
            }

            toolCall.Remove("function");
        }
    }

    /// <summary>
    /// When tool arguments are a JSON string, promote to a structured node so the
    /// compressor sees readable objects instead of escaped wire text.
    /// </summary>
    private static JsonNode? ExpandArgumentsIfJson(JsonNode? arguments)
    {
        if (arguments is not JsonValue value ||
            !value.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return arguments?.DeepClone();
        }

        try
        {
            var parsed = JsonNode.Parse(text);
            return parsed ?? arguments.DeepClone();
        }
        catch (JsonException)
        {
            return arguments.DeepClone();
        }
    }
}
