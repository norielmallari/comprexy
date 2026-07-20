using System.Net.Http;
using Comprexy.Infrastructure.Providers;

namespace Comprexy.Application.Tests.Services;

public class UpstreamStreamEndDetectorTests
{
    [Fact]
    public void IsPrematureUpstreamStreamEnd_DetectsHttpResponseEnded()
    {
        var ex = new TaskCanceledException(
            "The operation was canceled.",
            new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."));

        Assert.True(UpstreamStreamEndDetector.IsPrematureUpstreamStreamEnd(ex));
    }

    [Fact]
    public void IsPrematureUpstreamStreamEnd_IgnoresUnrelatedCancellation()
    {
        var ex = new TaskCanceledException("The operation was canceled.");
        Assert.False(UpstreamStreamEndDetector.IsPrematureUpstreamStreamEnd(ex));
    }
}
