using Comprexy.Application.Abstractions;

namespace Comprexy.Infrastructure.Persistence;

public class EfUnitOfWork(ComprexyDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
