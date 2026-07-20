namespace Comprexy.Infrastructure.Providers;

/// <summary>
/// Detects upstream HTTP streams that closed without a clean SSE end.
/// </summary>
public static class UpstreamStreamEndDetector
{
    public static bool IsPrematureUpstreamStreamEnd(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded })
            {
                return true;
            }

            if (current.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
