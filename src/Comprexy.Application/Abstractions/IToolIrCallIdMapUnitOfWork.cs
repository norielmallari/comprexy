namespace Comprexy.Application.Abstractions;

/// <summary>
/// Short-lived unit of work for dual-id map rows only. Does not share the chat-scoped DbContext.
/// </summary>
public interface IToolIrCallIdMapUnitOfWork : IAsyncDisposable
{
    IConversationToolCallMapRepository Maps { get; }

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
