using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Instruments.Base;
using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Application.Commands.Instruments.SyncTInvestShares;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Syncs the catalog of every exchange <see cref="InstrumentSyncSettings" /> enables when the app starts and then every
///     <see cref="InstrumentSyncSettings.IntervalMinutes" />. The exchanges sync at the same time, each at its own pace: the categories of
///     Bybit one after another, the shares of T-Invest alongside. The settings are read again before every sync, so a change of
///     appsettings.json applies without a restart.
/// </summary>
internal sealed class InstrumentSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<InstrumentSyncSettings> settings,
    TimeProvider time,
    ILogger<InstrumentSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        InstrumentSyncSettings sync = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            // The wait starts with the sync, so the syncs keep to the interval however long one takes.
            var wait = Task.Delay(TimeSpan.FromMinutes(sync.IntervalMinutes), time, stoppingToken);

            await Task.WhenAll(SyncBybitAsync(sync.Bybit, stoppingToken), SyncTInvestAsync(sync.TInvest, stoppingToken));
            await wait;

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // One request at a time to Bybit: its categories share the limits of the exchange.
    private async Task SyncBybitAsync(BybitInstrumentSyncSettings bybit, CancellationToken stoppingToken)
    {
        if (!bybit.Enabled)
        {
            return;
        }

        foreach (BybitCategory category in bybit.Categories)
        {
            await SendAsync(new SyncBybitInstrumentsCommand(category, time.GetUtcNow()), $"the {category} instruments of Bybit", stoppingToken);
        }
    }

    private async Task SyncTInvestAsync(ExchangeSyncSettings tinvest, CancellationToken stoppingToken)
    {
        if (tinvest.Enabled)
        {
            await SendAsync(new SyncTInvestSharesCommand(time.GetUtcNow()), "the shares of T-Invest", stoppingToken);
        }
    }

    // A sync that fails is logged and waits for the next one; the others go on. An escaping exception would stop the host. Each sync has
    // a scope of its own, so the syncs of the two exchanges do not share a connection or a transaction.
    private async Task SendAsync(BaseSyncInstrumentsCommand command, string what, CancellationToken stoppingToken)
    {
        try
        {
            await scopes.SendAsync(command, stoppingToken);
            logger.LogInformation("Synced {What}.", what);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Sync of {What} failed.", what);
        }
    }
}