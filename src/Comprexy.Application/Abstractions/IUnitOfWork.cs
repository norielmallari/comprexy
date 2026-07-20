namespace Comprexy.Application.Abstractions;

/// <summary>
/// Commits changes made across repositories within a single logical operation.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
