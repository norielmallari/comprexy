using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comprexy.Infrastructure.Persistence.Converters;

/// <summary>
/// Persists <see cref="DateTimeOffset"/> as UTC ticks (<see cref="long"/>) so SQLite can
/// filter/order timestamps server-side (SQLite cannot ORDER BY DateTimeOffset expressions).
/// </summary>
public sealed class DateTimeOffsetToUtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    static v => v.UtcTicks,
    static v => new DateTimeOffset(v, TimeSpan.Zero));

/// <summary>Nullable twin of <see cref="DateTimeOffsetToUtcTicksConverter"/>.</summary>
public sealed class NullableDateTimeOffsetToUtcTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    static v => v.HasValue ? v.Value.UtcTicks : null,
    static v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
