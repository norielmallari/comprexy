using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Persistence.Repositories;
using Comprexy.Infrastructure.Providers;
using Comprexy.Infrastructure.Settings;
using Comprexy.Infrastructure.Time;
using Comprexy.Infrastructure.Tokenization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comprexy.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComprexyInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Comprexy")
            ?? "Data Source=comprexy.db;Cache=Shared";
        services.AddSingleton<SqliteWalConnectionInterceptor>();
        services.AddSingleton<ClusterIdSaveChangesInterceptor>();
        // Factory is Singleton and needs Singleton DbContextOptions; keep scoped DbContext.
        services.AddDbContext<ComprexyDbContext>(
            (sp, options) => ConfigureComprexyDbContext(options, connectionString, sp),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<ComprexyDbContext>((sp, options) =>
            ConfigureComprexyDbContext(options, connectionString, sp));

        services.AddScoped<IConversationRepository, EfConversationRepository>();
        services.AddScoped<IConversationMessageRepository, EfConversationMessageRepository>();
        services.AddScoped<IWorkingMemoryRepository, EfWorkingMemoryRepository>();
        services.AddScoped<ICompressionEventRepository, EfCompressionEventRepository>();
        services.AddScoped<IConversationTurnMetricRepository, EfConversationTurnMetricRepository>();
        services.AddScoped<IConversationMetricsSummaryRepository, EfConversationMetricsSummaryRepository>();
        services.AddScoped<IConversationToolCatalogRepository, EfConversationToolCatalogRepository>();
        services.AddScoped<IConversationToolDefinitionRepository, EfConversationToolDefinitionRepository>();
        // ConversationToolCallMap rows are staged only via IToolIrCallIdMapUnitOfWork (isolated context).
        services.AddSingleton<IToolIrCallIdMapUnitOfWorkFactory, EfToolIrCallIdMapUnitOfWorkFactory>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IModelPricingCatalogQuery, ModelPricingCatalogQuery>();
        services.AddScoped<IOperatorSettingsStore, OperatorSettingsStore>();

        services.AddSingleton<IOperatorSettingsOverlay, OperatorSettingsOverlay>();
        services.AddSingleton<OperatorSettingsChangeTokenSource>();
        services.AddHostedService<OperatorSettingsRevisionWatcher>();
        RegisterOperatorSettingsOverlay<ProxyOptions>(services);
        RegisterOperatorSettingsOverlay<ContextPolicyOptions>(services);
        RegisterOperatorSettingsOverlay<CacheAlignmentOptions>(services);
        RegisterOperatorSettingsOverlay<MetricsOptions>(services);
        RegisterOperatorSettingsOverlay<ToolSchemaOptions>(services);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenEstimateCache, TokenEstimateCache>();
        services.AddSingleton<ITokenEstimator, TiktokenTokenEstimator>();

        var providerOptions = configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ?? new ProviderOptions();
        var compressionOptions = configuration.GetSection(CompressionOptions.SectionName).Get<CompressionOptions>() ?? new CompressionOptions();
        var longestTimeoutSeconds = Math.Max(
            providerOptions.TimeoutSeconds,
            compressionOptions.TimeoutSeconds ?? providerOptions.TimeoutSeconds);
        services.AddHttpClient<OpenAiCompatibleChatCompletionClient>(client =>
        {
            // HttpClient timeout must exceed per-request CTS timeouts for chat and compression.
            client.Timeout = TimeSpan.FromSeconds(Math.Max(longestTimeoutSeconds, 120) + 30);
        });
        services.AddTransient<IChatCompletionClient>(sp => new UpstreamActivityTrackingChatCompletionClient(
            sp.GetRequiredService<OpenAiCompatibleChatCompletionClient>(),
            sp.GetRequiredService<IUpstreamActivityGate>()));
        services.AddHttpClient<IUpstreamPassthroughProxy, UpstreamPassthroughProxy>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(longestTimeoutSeconds, 120) + 30);
        });

        return services;
    }

    private static void RegisterOperatorSettingsOverlay<TOptions>(IServiceCollection services)
        where TOptions : class
    {
        services.AddSingleton<IConfigureOptions<TOptions>, OperatorSettingsOverlayConfigureOptions<TOptions>>();
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>, OperatorSettingsChangeTokenSource<TOptions>>();
    }

    private static void ConfigureComprexyDbContext(
        DbContextOptionsBuilder options,
        string connectionString,
        IServiceProvider sp)
    {
        options.UseSqlite(connectionString, sqlite =>
            sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        options.AddInterceptors(
            sp.GetRequiredService<SqliteWalConnectionInterceptor>(),
            sp.GetRequiredService<ClusterIdSaveChangesInterceptor>());
        // EF Core 3+ does not silently client-evaluate queries (untranslatable LINQ throws).
        // Escalate remaining warnings so query/shape issues cannot be ignored accidentally.
        options.ConfigureWarnings(warnings =>
        {
            warnings.Default(WarningBehavior.Throw);
            warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning);
            warnings.Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning);
            warnings.Ignore(RelationalEventId.AmbientTransactionWarning);
            warnings.Ignore(CoreEventId.FirstWithoutOrderByAndFilterWarning);
        });
    }
}
