using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Infrastructure.BackgroundJobs;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Persistence.Repositories;
using Comprexy.Infrastructure.Providers;
using Comprexy.Infrastructure.Time;
using Comprexy.Infrastructure.Tokenization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comprexy.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComprexyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Comprexy")
            ?? "Data Source=comprexy.db;Cache=Shared";
        services.AddSingleton<SqliteWalConnectionInterceptor>();
        services.AddDbContext<ComprexyDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString, sqlite =>
                sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            options.AddInterceptors(sp.GetRequiredService<SqliteWalConnectionInterceptor>());
        });

        services.AddScoped<IConversationRepository, EfConversationRepository>();
        services.AddScoped<IConversationMessageRepository, EfConversationMessageRepository>();
        services.AddScoped<IWorkingMemoryRepository, EfWorkingMemoryRepository>();
        services.AddScoped<ICompressionEventRepository, EfCompressionEventRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenEstimateCache, TokenEstimateCache>();
        services.AddSingleton<ITokenEstimator, TiktokenTokenEstimator>();

        var providerOptions = configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ?? new ProviderOptions();
        var compressionOptions = configuration.GetSection(CompressionOptions.SectionName).Get<CompressionOptions>() ?? new CompressionOptions();
        var longestTimeoutSeconds = Math.Max(
            providerOptions.TimeoutSeconds,
            compressionOptions.TimeoutSeconds ?? providerOptions.TimeoutSeconds);
        services.AddHttpClient<IChatCompletionClient, OpenAiCompatibleChatCompletionClient>(client =>
        {
            // HttpClient timeout must exceed per-request CTS timeouts for chat and compression.
            client.Timeout = TimeSpan.FromSeconds(Math.Max(longestTimeoutSeconds, 120) + 30);
        });
        services.AddHttpClient<IUpstreamPassthroughProxy, UpstreamPassthroughProxy>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(longestTimeoutSeconds, 120) + 30);
        });

        services.AddSingleton<ICompressionQueue, ChannelCompressionQueue>();
        services.AddHostedService<CompressionBackgroundService>();

        return services;
    }
}
