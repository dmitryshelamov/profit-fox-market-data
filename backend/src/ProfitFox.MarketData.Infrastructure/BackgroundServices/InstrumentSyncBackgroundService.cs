using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Syncs the catalog of every category of <see cref="InstrumentSyncSettings" /> when the app starts and then every
///     <see cref="InstrumentSyncSettings.IntervalMinutes" />. The settings are read again before every sync, so a change of
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

            await SyncAsync(sync.Categories, stoppingToken);
            await wait;

            sync = ReadSettings(sync);
        }
    }

    // A broken change (an interval of zero, an unknown category) is logged, and the last valid settings stay until it is fixed.
    private InstrumentSyncSettings ReadSettings(InstrumentSyncSettings lastValid)
    {
        try
        {
            return settings.CurrentValue;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
        {
            logger.LogError(exception, "The InstrumentSync settings are broken; the sync goes on with the last valid ones.");

            return lastValid;
        }
    }

    // A category that fails is logged and waits for the next sync; the others go on. An escaping exception would stop the host.
    private async Task SyncAsync(IReadOnlyList<BybitCategory> categories, CancellationToken stoppingToken)
    {
        foreach (BybitCategory category in categories)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

                await sender.Send(new SyncBybitInstrumentsCommand(category, time.GetUtcNow()), stoppingToken);
                logger.LogInformation("Synced the {Category} instruments of Bybit.", category);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Sync of the {Category} instruments of Bybit failed.", category);
            }
        }
    }
}