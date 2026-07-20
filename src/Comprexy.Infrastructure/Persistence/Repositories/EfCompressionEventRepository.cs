using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfCompressionEventRepository(ComprexyDbContext dbContext) : ICompressionEventRepository
{
    public void Add(CompressionEvent compressionEvent) => dbContext.CompressionEvents.Add(compressionEvent);
}
