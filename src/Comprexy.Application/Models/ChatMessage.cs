using System.Text.Json;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Models;

/// <summary>
/// Internal representation of a chat message. <see cref="Content"/> is the text used for
/// persistence and compression. When <see cref="RawWireMessage"/> is set, token estimation and
/// upstream forwarding use that exact OpenAI message object (tool_calls, multimodal parts, etc.).
/// </summary>
public sealed record ChatMessage(
    MessageRole Role,
    string Content,
    JsonElement? RawWireMessage = null);
