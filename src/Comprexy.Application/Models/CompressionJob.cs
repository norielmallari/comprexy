using Comprexy.Domain.Enums;

namespace Comprexy.Application.Models;

/// <summary>
/// A unit of work enqueued for the background compression worker.
/// </summary>
public sealed record CompressionJob(Guid ConversationId, CompressionMode Mode);
