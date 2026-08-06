using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Comprexy.Infrastructure.Settings;

/// <summary>
/// Polls OperatorSettings revision and reloads overlay + change tokens on advance.
/// Creates a DI scope per poll only; does not dispose container-owned resources.
/// </summary>
public sealed class OperatorSettingsRevisionWatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperatorSettingsOverlay _overlay;
    private readonly OperatorSettingsChangeTokenSource _changeTokens;
    private readonly IOptionsMonitor<OperatorSettingsOptions> _options;
    private readonly ILogger<OperatorSettingsRevisionWatcher> _logger;

    public OperatorSettingsRevisionWatcher(
        IServiceScopeFactory scopeFactory,
        IOperatorSettingsOverlay overlay,
        OperatorSettingsChangeTokenSource changeTokens,
        IOptionsMonitor<OperatorSettingsOptions> options,
        ILogger<OperatorSettingsRevisionWatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _overlay = overlay;
        _changeTokens = changeTokens;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load so CurrentValue reflects SQLite before the first poll delay.
        await PollOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _options.CurrentValue.PollInterval;
            if (delay < TimeSpan.FromMilliseconds(250))
            {
                delay = TimeSpan.FromMilliseconds(250);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await PollOnceAsync(stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOperatorSettingsStore>();
            var (revision, json, _) = await store.GetAsync(cancellationToken);
            if (_overlay.TryUpdate(revision, json))
            {
                _changeTokens.Signal();
                _logger.LogInformation(
                    "Operator settings overlay updated to revision {Revision}.",
                    revision);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operator settings revision poll failed.");
        }
    }
}

/// <summary>
/// Single change-token source shared by all allowlisted option types.
/// </summary>
public sealed class OperatorSettingsChangeTokenSource
{
    private CancellationTokenSource _cts = new();

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_cts.Token);

    public void Signal()
    {
        var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}

public sealed class OperatorSettingsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
{
    private readonly OperatorSettingsChangeTokenSource _source;

    public OperatorSettingsChangeTokenSource(OperatorSettingsChangeTokenSource source)
    {
        _source = source;
    }

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => _source.GetChangeToken();
}
