using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComprexyApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<ProviderOptions>, ProviderOptionsValidator>();
        services.AddOptions<ProviderOptions>()
            .Bind(configuration.GetSection(ProviderOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CompressionOptions>()
            .Bind(configuration.GetSection(CompressionOptions.SectionName));

        services.AddOptions<ContextPolicyOptions>()
            .Bind(configuration.GetSection(ContextPolicyOptions.SectionName));

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName));

        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName));

        services.AddOptions<TraceOptions>()
            .Bind(configuration.GetSection(TraceOptions.SectionName));

        services.AddOptions<TokenEstimateCacheOptions>()
            .Bind(configuration.GetSection(TokenEstimateCacheOptions.SectionName));

        services.AddOptions<ToolSchemaOptions>()
            .Bind(configuration.GetSection(ToolSchemaOptions.SectionName));

        services.AddOptions<MetricsOptions>()
            .Bind(configuration.GetSection(MetricsOptions.SectionName));

        services.AddSingleton<IRequestTraceFileSession, RequestTraceFileSession>();
        services.AddSingleton<IPayloadTraceLogger, PayloadTraceLogger>();
        services.AddSingleton<IConversationIdentityResolver, ConversationIdentityResolver>();
        services.AddSingleton<IConversationRequestGate, ConversationRequestGate>();
        services.AddSingleton<ContextBudgetEvaluator>();
        services.AddSingleton<ContextBuilder>();
        services.AddSingleton<RecentContextSelector>();
        services.AddSingleton<CompressionPromptFactory>();
        services.AddSingleton<ToolSchemaPromptFactory>();
        services.AddSingleton<ToolCatalogParser>();
        services.AddSingleton<ToolArgumentValidator>();
        services.AddScoped<ToolSchemaOrchestrator>();
        services.AddSingleton<ProviderEndpointResolver>();
        services.AddScoped<ICompressionOrchestrator, CompressionOrchestrator>();
        services.AddScoped<IConversationMetricsRecorder, ConversationMetricsRecorder>();
        services.AddScoped<IConversationMetricsQueryService, ConversationMetricsQueryService>();
        services.AddScoped<ProxyChatCompletionService>();

        return services;
    }
}
