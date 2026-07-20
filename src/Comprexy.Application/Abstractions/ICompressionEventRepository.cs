using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface ICompressionEventRepository
{
    void Add(CompressionEvent compressionEvent);
}
