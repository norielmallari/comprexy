using Comprexy.Application.Configuration;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Request-scoped prompt token basis for metrics reads. Endpoints may set
/// <see cref="RequestOverride"/> from <c>?promptTokenBasis=</c>; otherwise
/// <see cref="MetricsOptions.PromptTokenBasis"/> applies.
/// </summary>
public sealed class PromptTokenBasisContext(IOptionsMonitor<MetricsOptions> options)
{
    public PromptTokenBasis? RequestOverride { get; set; }

    public PromptTokenBasis Resolve() =>
        RequestOverride ?? options.CurrentValue.PromptTokenBasis;
}
