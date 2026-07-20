namespace Comprexy.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
