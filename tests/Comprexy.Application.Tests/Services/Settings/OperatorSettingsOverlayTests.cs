using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services.Settings;

public class OperatorSettingsOverlayTests
{
    [Fact]
    public void OverlayApply_AfterSignal_CurrentValueSeesSoftLimitAndMode_NoSleep()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextPolicy:SoftLimitTokens"] = "100",
                ["Proxy:OptimizationMode"] = "Full"
            })
            .Build();

        var overlay = new OperatorSettingsOverlay();
        var changeTokens = new OperatorSettingsChangeTokenSource();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IOperatorSettingsOverlay>(overlay);
        services.AddSingleton(changeTokens);
        services.AddOptions<ContextPolicyOptions>().Bind(configuration.GetSection("ContextPolicy"));
        services.AddOptions<ProxyOptions>().Bind(configuration.GetSection("Proxy"));
        services.AddSingleton<IConfigureOptions<ContextPolicyOptions>, OperatorSettingsOverlayConfigureOptions<ContextPolicyOptions>>();
        services.AddSingleton<IConfigureOptions<ProxyOptions>, OperatorSettingsOverlayConfigureOptions<ProxyOptions>>();
        services.AddSingleton<IOptionsChangeTokenSource<ContextPolicyOptions>, OperatorSettingsChangeTokenSource<ContextPolicyOptions>>();
        services.AddSingleton<IOptionsChangeTokenSource<ProxyOptions>, OperatorSettingsChangeTokenSource<ProxyOptions>>();

        using var provider = services.BuildServiceProvider();
        var contextMonitor = provider.GetRequiredService<IOptionsMonitor<ContextPolicyOptions>>();
        var proxyMonitor = provider.GetRequiredService<IOptionsMonitor<ProxyOptions>>();

        Assert.Equal(100, contextMonitor.CurrentValue.SoftLimitTokens);
        Assert.Equal(OptimizationMode.Full, proxyMonitor.CurrentValue.OptimizationMode);

        var dto = new OperatorMutableSettingsDto
        {
            Proxy = new ProxyMutableDto { OptimizationMode = OptimizationMode.MonitorOnly },
            ContextPolicy = new ContextPolicyMutableDto { SoftLimitTokens = 777 }
        };
        var json = OperatorMutableSettingsJson.Serialize(dto);
        Assert.True(overlay.TryUpdate(revision: 1, json));
        changeTokens.Signal();

        Assert.Equal(777, contextMonitor.CurrentValue.SoftLimitTokens);
        Assert.Equal(OptimizationMode.MonitorOnly, proxyMonitor.CurrentValue.OptimizationMode);
    }

    [Fact]
    public void OverlayTryUpdate_SameRevisionSameJson_ReturnsFalse()
    {
        var overlay = new OperatorSettingsOverlay();
        var json = """{"proxy":{"optimizationMode":"monitorOnly"}}""";
        Assert.True(overlay.TryUpdate(1, json));
        Assert.False(overlay.TryUpdate(1, json));
    }
}
