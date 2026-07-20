using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Runs a single compression attempt (background or emergency) for a conversation.
/// </summary>
public interface ICompressionOrchestrator
{
    Task<CompressionEvent?> RunAsync(Guid conversationId, CompressionMode mode, CancellationToken cancellationToken);
}
