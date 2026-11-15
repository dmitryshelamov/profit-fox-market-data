using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the history of open interest of every listed linear contract of Bybit, back to
///     <see cref="OpenInterestSyncSettings.HistoryDays" />, and the steps the collection missed, while
///     <see cref="OpenInterestSyncSettings" /> enable Bybit. A round loads one page of every contract,
///     <see cref="OpenInterestSyncSettings.Parallelism" /> contracts at a time, so every contract moves on at the same pace. Rounds
///     follow one another while pages are left; then the service waits <see cref="OpenInterestSyncSettings.IdleSeconds" />. A round in
///     which the collection missed nothing asks Bybit nothing. The settings are read again before every round.
/// </summary>
internal sealed class OpenInterestSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<OpenInterestSyncSettings> settings,
    TimeProvider time,
    ILogger<OpenInterestSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        OpenInterestSyncSettings sync = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool hasMorePages = sync.Bybit.Enabled && await LoadRoundAsync(sync, stoppingToken);

            if (!hasMorePages)
            {
                await Task.Delay(TimeSpan.FromSeconds(sync.IdleSeconds), time, stoppingToken);
            }

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // A round: one page of every listed linear contract; whether any of them has more to load. A pause of Bybit is waited out by the
    // page that met it, and its contract is asked again in the next round. A page that fails for a reason of its own is logged and its
    // contract is tried again after the idle wait. Bybit being unavailable stops the round: every other contract would fail the same
    // way after retries of its own, so Bybit is tried again after the idle wait.
    private async Task<bool> LoadRoundAsync(OpenInterestSyncSettings sync, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);
        DateTimeOffset from = time.GetUtcNow().AddDays(-sync.HistoryDays);
        var hasMorePages = 0;
        var unavailable = 0;

        await Parallel.ForEachAsync(
            instruments.Where(HasOpenInterest),
            new ParallelOptions { MaxDegreeOfParallelism = sync.Parallelism, CancellationToken = stoppingToken },
            async (instrument, token) =>
            {
                if (Volatile.Read(ref unavailable) != 0)
                {
                    return;
                }

                try
                {
                    LoadOpenInterestResult page = await scopes.SendAsync(new LoadOpenInterestCommand(instrument.Id, from, time.GetUtcNow()), token);

                    if (page == LoadOpenInterestResult.MoreLeft)
                    {
                        Interlocked.Exchange(ref hasMorePages, value: 1);
                    }
                }
                catch (ExchangePausedException exception) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning("Bybit paused the loading of open interest until {Until:O}: {Reason}", exception.Until, exception.Message);
                    await WaitUntilAsync(exception.Until, token);
                    Interlocked.Exchange(ref hasMorePages, value: 1);
                }
                catch (TransientExchangeException exception) when (!token.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref unavailable, value: 1) == 0)
                    {
                        logger.LogError(exception, "Bybit is unavailable; the loading of its open interest goes on after the idle wait.");
                    }
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    logger.LogError(exception, "Loading the open interest of instrument {InstrumentId} of Bybit failed.", instrument.Id);
                }
            });

        return hasMorePages != 0 && unavailable == 0;
    }

    // A linear contract has open interest, perpetual or dated; a spot pair has none.
    private static bool HasOpenInterest(InstrumentInternal instrument)
    {
        return instrument.DelistedAt is null && instrument.Listing is BybitListedInstrumentInternal { Category: BybitCategory.Linear };
    }

    private async Task WaitUntilAsync(DateTimeOffset until, CancellationToken stoppingToken)
    {
        TimeSpan wait = until - time.GetUtcNow();

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, stoppingToken);
        }
    }
}