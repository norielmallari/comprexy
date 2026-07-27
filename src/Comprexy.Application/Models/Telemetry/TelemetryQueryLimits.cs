namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Default and maximum row bounds for telemetry turn projections.
/// Callers must clamp requested take values before composing EF <c>Take(...)</c>.
/// </summary>
public static class TelemetryQueryLimits
{
    public const int DefaultTake = 100;

    public const int MaxTake = 1000;

    public static int ClampTake(int? requestedTake, int defaultTake = DefaultTake, int maxTake = MaxTake)
    {
        var take = requestedTake ?? defaultTake;
        if (take < 1)
        {
            take = defaultTake;
        }

        return Math.Min(take, maxTake);
    }
}
