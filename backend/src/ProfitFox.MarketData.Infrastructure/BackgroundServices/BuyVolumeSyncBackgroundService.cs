using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Fills the buy volumes the stream did not tell: of the history, back to <see cref="BuyVolumeSyncSettings.HistoryDays" />, and of the
///     minutes of a downtime, from the daily archives of trades of Bybit, while <see cref="BuyVolumeSyncSettings" /> enables Bybit. A round
///     settles one archive of every listed instrument, the newest day first, <see cref="BuyVolumeSyncSettings.Parallelism" /> instruments
///     at a time, so fresh days come before old history and every instrument moves on at the same pace. Rounds follow one another while
///     days are left; then the service waits <see cref="BuyVolumeSyncSettings.IdleSeconds" />, by which a new day may be published. The
///     settings are read again before every round.
/// </summary>
internal sealed class BuyVolumeSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<BuyVolumeSyncSettings> settings,
    TimeProvider time,
    ILogger<BuyVolumeSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        BuyVolumeSyncSettings sync = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool hasMoreDays = sync.Bybit.Enabled && await SettleRoundAsync(sync, stoppingToken);

            if (!hasMoreDays)
            {
                await Task.Delay(TimeSpan.FromSeconds(sync.IdleSeconds), time, stoppingToken);
            }

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // A round: one command for every listed instrument of Bybit; whether any of them has more days to settle. An archive that is
    // unavailable stops the round: every other instrument would fail the same way after retries of its own, so it is tried again after
    // the idle wait.
    private async Task<bool> SettleRoundAsync(BuyVolumeSyncSettings sync, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await InstrumentsAsync(stoppingToken);
        MinuteRange history = History(sync.HistoryDays);
        var hasMoreDays = 0;
        var unavailable = 0;

        await Parallel.ForEachAsync(
            instruments.Where(instrument => instrument.DelistedAt is null),
            new ParallelOptions { MaxDegreeOfParallelism = sync.Parallelism, CancellationToken = stoppingToken },
            async (instrument, token) =>
            {
                if (Volatile.Read(ref unavailable) != 0)
                {
                    return;
                }

                try
                {
                    if (await scopes.SendAsync(new LoadBuyVolumesCommand(instrument.Id, history, time.GetUtcNow()), token) == LoadBuyVolumesResult.MoreLeft)
                    {
                        Interlocked.Exchange(ref hasMoreDays, value: 1);
                    }
                }
                catch (TransientExchangeException exception) when (!token.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref unavailable, value: 1) == 0)
                    {
                        logger.LogError(exception, "The archive of trades of Bybit is unavailable; the buy volumes go on after the idle wait.");
                    }
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    logger.LogError(exception, "Loading the buy volumes of instrument {InstrumentId} failed.", instrument.Id);
                }
            });

        return hasMoreDays != 0 && unavailable == 0;
    }

    // The instruments of Bybit in the catalog, listed or delisted; none when the catalog cannot be read, which is logged.
    private async Task<IReadOnlyCollection<InstrumentInternal>> InstrumentsAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await scopes.SendAsync(new GetInstrumentsQuery(Exchange.Bybit), stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading the instruments of Bybit to load their buy volumes failed.");

            return [];
        }
    }

    // The whole minutes from historyDays back to now.
    private MinuteRange History(int historyDays)
    {
        DateTimeOffset now = time.GetUtcNow();

        return new MinuteRange(MinuteRange.StartOfMinute(now.AddDays(-historyDays)), MinuteRange.StartOfMinute(now));
    }
}