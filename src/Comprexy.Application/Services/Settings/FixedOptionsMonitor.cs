using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Fixed-value <see cref="IOptionsMonitor{T}"/> for test / compat constructors that still pass
/// <see cref="IOptions{T}"/>.
/// </summary>
public sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    public FixedOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public FixedOptionsMonitor(IOptions<T> options)
        : this(options.Value)
    {
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
