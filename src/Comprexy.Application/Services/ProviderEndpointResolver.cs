using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Resolves concrete <see cref="ProviderEndpoint"/> values for the upstream chat model and the
/// (possibly distinct) compression model, falling back to the upstream provider when compression
/// settings are unset.
/// </summary>
public class ProviderEndpointResolver
{
    private readonly ProviderOptions _provider;
    private readonly CompressionOptions _compression;

    public ProviderEndpointResolver(IOptions<ProviderOptions> provider, IOptions<CompressionOptions> compression)
    {
        _provider = provider.Value;
        _compression = compression.Value;
    }

    public ProviderEndpoint ResolveUpstream() =>
        new(_provider.BaseUrl, _provider.ApiKey, _provider.Model, _provider.TimeoutSeconds);

    public ProviderEndpoint ResolveCompression() =>
        new(
            _compression.BaseUrl ?? _provider.BaseUrl,
            _compression.ApiKey ?? _provider.ApiKey,
            _compression.Model ?? _provider.Model,
            _compression.TimeoutSeconds ?? _provider.TimeoutSeconds);
}
