using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Runs a single compression attempt (background or emergency) for a conversation.
/// </summary>
public interface ICompressionOrchestrator
{
    /// <param name="preferredModel">
    /// Used when neither <c>Compression:Model</c> nor <c>Provider:Model</c> is configured
    /// (usually the client chat model for this turn).
    /// </param>
    Task<CompressionEvent?> RunAsync(
        Guid conversationId,
        CompressionMode mode,
        CancellationToken cancellationToken,
        string? preferredModel = null);
}
