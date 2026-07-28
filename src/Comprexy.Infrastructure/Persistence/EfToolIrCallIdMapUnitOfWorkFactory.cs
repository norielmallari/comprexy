using Comprexy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence;

public sealed class EfToolIrCallIdMapUnitOfWorkFactory(IDbContextFactory<ComprexyDbContext> dbContextFactory)
    : IToolIrCallIdMapUnitOfWorkFactory
{
    public IToolIrCallIdMapUnitOfWork Create() =>
        new EfToolIrCallIdMapUnitOfWork(dbContextFactory.CreateDbContext());
}
