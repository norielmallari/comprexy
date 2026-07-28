using Comprexy.Application.Abstractions;
using Comprexy.Infrastructure.Persistence.Repositories;

namespace Comprexy.Infrastructure.Persistence;

/// <summary>Owns a factory-created <see cref="ComprexyDbContext"/> used only for dual-id map rows.</summary>
public sealed class EfToolIrCallIdMapUnitOfWork : IToolIrCallIdMapUnitOfWork
{
    private readonly ComprexyDbContext _dbContext;

    public EfToolIrCallIdMapUnitOfWork(ComprexyDbContext dbContext)
    {
        _dbContext = dbContext;
        Maps = new EfConversationToolCallMapRepository(dbContext);
    }

    public IConversationToolCallMapRepository Maps { get; }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => _dbContext.DisposeAsync();
}
