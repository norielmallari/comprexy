using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Application.Services.Rules;
using Comprexy.Application.Services.Settings;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <param name="enableProxyServices">
    /// When true (proxy default), registers chat/compression application services.
    /// Control-plane hosts should pass false and only keep options + metrics query.
    /// </param>
    public static IServiceCollection AddComprexyApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableProxyServices = true)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName));

        services.AddOptions<MetricsOptions>()
            .Bind(configuration.GetSection(MetricsOptions.SectionName));

        services.AddOptions<OperatorSettingsOptions>()
            .Bind(configuration.GetSection(OperatorSettingsOptions.SectionName));

        services.AddSingleton<IEvidenceMarkdownService, EvidenceMarkdownService>();
        services.AddSingleton<IRegressionDetector, RegressionDetector>();
        services.AddSingleton<IBenchmarkTotalsCalculator, BenchmarkTotalsCalculator>();
        services.AddSingleton<IBenchmarkCostCalculator, BenchmarkCostCalculator>();
        services.AddScoped<PromptTokenBasisContext>();
        services.AddScoped<IConversationMetricsQueryService, ConversationMetricsQueryService>();
        services.AddScoped<IConversationRetrievalQueryService, ConversationRetrievalQueryService>();
        services.AddScoped<IEffectiveSettingsAccessor, EffectiveSettingsAccessor>();

        // Allowlisted options for SQLite overlay / typed HttpClient on both hosts.
        // Full proxy services (chat prepare path) remain gated by enableProxyServices.
        services.AddOptions<ContextPolicyOptions>()
            .Bind(configuration.GetSection(ContextPolicyOptions.SectionName));
        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName));
        services.AddOptions<ToolSchemaOptions>()
            .Bind(configuration.GetSection(ToolSchemaOptions.SectionName));
        services.AddOptions<CacheAlignmentOptions>()
            .Bind(configuration.GetSection(CacheAlignmentOptions.SectionName));

        // Required by Infrastructure's IChatCompletionClient decorator even when proxy services are off.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IUpstreamActivityGate, UpstreamActivityGate>();

        if (!enableProxyServices)
        {
            return services;
        }

        services.AddSingleton<IValidateOptions<ProviderOptions>, ProviderOptionsValidator>();
        services.AddOptions<ProviderOptions>()
            .Bind(configuration.GetSection(ProviderOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CompressionOptions>()
            .Bind(configuration.GetSection(CompressionOptions.SectionName));

        services.AddOptions<TraceOptions>()
            .Bind(configuration.GetSection(TraceOptions.SectionName));

        services.AddOptions<TokenEstimateCacheOptions>()
            .Bind(configuration.GetSection(TokenEstimateCacheOptions.SectionName));

        services.AddSingleton<IRequestTraceFileSession, RequestTraceFileSession>();
        services.AddSingleton<IPayloadTraceLogger, PayloadTraceLogger>();
        services.AddSingleton<IConversationIdentityResolver, ConversationIdentityResolver>();
        services.AddSingleton<IConversationRequestGate, ConversationRequestGate>();
        services.AddSingleton<ICacheAlignmentService, CacheAlignmentService>();
        services.AddSingleton<ContextBudgetEvaluator>();
        services.AddSingleton<ContextBuilder>();
        services.AddSingleton<ISystemRulesDetector, SystemRulesDetector>();
        services.AddSingleton<ITranscriptRulesDetector, TranscriptRulesDetector>();
        services.AddSingleton<IRulesConsolidator, RulesConsolidator>();
        services.AddSingleton<IRulesInjector, RulesInjector>();
        services.AddSingleton<RecentContextSelector>();
        services.AddSingleton<CompressionPromptFactory>();
        services.AddSingleton<ToolCatalogParser>();
        services.AddSingleton<ToolArgumentValidator>();
        services.AddSingleton<ToolIrCallIdMap>();
        services.AddScoped<IToolIrCallIdMapService, ToolIrCallIdMapService>();
        services.AddSingleton<ToolIrFileBodyCache>();
        services.AddSingleton<ToolIrResultShapeStore>();
        services.AddSingleton<IToolIrShapeLearnQueue, ToolIrShapeLearnQueue>();
        services.AddSingleton<ToolIrPlanner>();
        services.AddSingleton<ToolIrResultDistiller>();
        services.AddScoped<ToolIrSchemaMapper>();
        services.AddScoped<ToolSchemaOrchestrator>();
        services.AddSingleton<ProviderEndpointResolver>();
        services.AddScoped<IConversationMetricsRecorder, ConversationMetricsRecorder>();
        services.AddScoped<ChatTurnMessageHelper>();
        services.AddScoped<ClientHistorySynchronizer>();
        services.AddScoped<OutgoingContextMaterializer>();
        services.AddScoped<IrFullPromptEstimator>();
        services.AddScoped<InlineWrapUpRunner>();
        services.AddScoped<ChatTurnPreparer>();
        services.AddScoped<ChatTurnCompleter>();
        services.AddScoped<ProxyChatCompletionService>();

        var learnerEnabled = configuration
            .GetSection(ToolSchemaOptions.SectionName)
            .GetSection("ResultShape")
            .GetSection("Learner")
            .GetValue("Enabled", defaultValue: true);
        if (learnerEnabled)
        {
            services.AddHostedService<ToolIrShapeLearnerService>();
        }

        return services;
    }
}
