using System.Text.Json;
using Comprexy.Application.Models;

namespace Comprexy.Application.Tests.Services;

public class ProviderEndpointTests
{
    [Fact]
    public void ResolveOutboundModel_PrefersConfiguredModel()
    {
        using var client = JsonDocument.Parse("""{"model":"client-model"}""");
        var endpoint = new ProviderEndpoint("http://localhost/v1", null, "provider-model", 60);

        Assert.Equal("provider-model", endpoint.ResolveOutboundModel(client.RootElement));
    }

    [Fact]
    public void ResolveOutboundModel_FallsBackToClientWhenProviderModelNull()
    {
        using var client = JsonDocument.Parse("""{"model":"client-model"}""");
        var endpoint = new ProviderEndpoint("http://localhost/v1", null, null, 60);

        Assert.Equal("client-model", endpoint.ResolveOutboundModel(client.RootElement));
        Assert.False(endpoint.HasConfiguredModel);
    }

    [Fact]
    public void WithPreferredModel_AppliesOnlyWhenUnset()
    {
        var configured = new ProviderEndpoint("http://localhost/v1", null, "provider", 60);
        var unset = new ProviderEndpoint("http://localhost/v1", null, null, 60);

        Assert.Equal("provider", configured.WithPreferredModel("client").Model);
        Assert.Equal("client", unset.WithPreferredModel("client").Model);
        Assert.Null(unset.WithPreferredModel(" ").Model);
    }

    [Fact]
    public void SharesKvCacheWith_RequiresConcreteMatchingModels()
    {
        var a = new ProviderEndpoint("http://localhost/v1", null, null, 60);
        var b = new ProviderEndpoint("http://localhost/v1", null, null, 60);
        var c = new ProviderEndpoint("http://localhost/v1", null, "m", 60);

        Assert.False(a.SharesKvCacheWith(b));
        Assert.False(a.SharesKvCacheWith(c));
        Assert.True(c.SharesKvCacheWith(new ProviderEndpoint("http://localhost/v1/", "key", "m", 120)));
    }
}
