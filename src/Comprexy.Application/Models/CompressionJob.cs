using Comprexy.Domain.Enums;

namespace Comprexy.Application.Models;

/// <summary>
/// A unit of work enqueued for the background compression worker.
/// </summary>
/// <param name="ConversationId">Conversation to compress.</param>
/// <param name="Mode">Soft / high-priority soft / emergency.</param>
/// <param name="PreferredModel">
/// Chat model used for this conversation when <c>Provider:Model</c> /
/// <c>Compression:Model</c> are unset (typically the client's request model).
/// </param>
public sealed record CompressionJob(
    Guid ConversationId,
    CompressionMode Mode,
    string? PreferredModel = null);
