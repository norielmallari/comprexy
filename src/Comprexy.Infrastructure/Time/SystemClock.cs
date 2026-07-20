using Comprexy.Application.Abstractions;

namespace Comprexy.Infrastructure.Time;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
