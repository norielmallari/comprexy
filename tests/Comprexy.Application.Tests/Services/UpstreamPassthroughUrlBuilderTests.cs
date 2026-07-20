using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class UpstreamPassthroughUrlBuilderTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1", "/v1/models", null, "http://localhost:11434/v1/models")]
    [InlineData("http://localhost:11434/v1/", "/v1/models", "?foo=1", "http://localhost:11434/v1/models?foo=1")]
    [InlineData("http://localhost:11434/v1", "/v1/embeddings", null, "http://localhost:11434/v1/embeddings")]
    [InlineData("http://localhost:11434", "/v1/models", null, "http://localhost:11434/v1/models")]
    [InlineData("http://localhost:11434/v1", "/v1", null, "http://localhost:11434/v1")]
    public void Build_MapsPathOntoProviderBase(string baseUrl, string path, string? query, string expected)
    {
        var uri = UpstreamPassthroughUrlBuilder.Build(baseUrl, path, query);
        Assert.Equal(expected, uri.ToString());
    }
}
