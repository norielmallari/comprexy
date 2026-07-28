using Comprexy.Application.Abstractions;

namespace Comprexy.Application.Tests.Services;

/// <summary>Test factory that always returns UoWs backed by the same in-memory map repository.</summary>
internal sealed class InMemoryToolIrCallIdMapUnitOfWorkFactory : IToolIrCallIdMapUnitOfWorkFactory
{
    private readonly InMemoryConversationToolCallMapRepository _repository;
    private readonly Func<Task>? _onSaveChanges;

    public InMemoryToolIrCallIdMapUnitOfWorkFactory(
        InMemoryConversationToolCallMapRepository repository,
        Func<Task>? onSaveChanges = null)
    {
        _repository = repository;
        _onSaveChanges = onSaveChanges;
    }

    public InMemoryConversationToolCallMapRepository Repository => _repository;

    public IToolIrCallIdMapUnitOfWork Create() =>
        new InMemoryToolIrCallIdMapUnitOfWork(_repository, _onSaveChanges);

    private sealed class InMemoryToolIrCallIdMapUnitOfWork(
        InMemoryConversationToolCallMapRepository repository,
        Func<Task>? onSaveChanges) : IToolIrCallIdMapUnitOfWork
    {
        public IConversationToolCallMapRepository Maps => repository;

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            onSaveChanges?.Invoke() ?? Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
