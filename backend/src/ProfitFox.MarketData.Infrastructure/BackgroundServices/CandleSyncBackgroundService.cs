using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the candles of every listed instrument of the exchanges <see cref="CandleSyncSettings" /> enables, back to
///     <see cref="CandleSyncSettings.HistoryDays" /> or, of those <see cref="FullHistorySettings" /> name, to when they started to trade,
///     and before them the ranges reads asked for and could not wait for (<see cref="CandleLoadQueue" />). A round loads one page of every
///     such range in turn, then one page of every instrument, <see cref="CandleSyncSettings.Parallelism" /> instruments at a time, the
///     newest it misses first, so fresh minutes come before old history and every instrument moves on at the same pace. Rounds follow
///     one another while pages are left; an exchange with none left waits <see cref="CandleSyncSettings.IdleSeconds" />. The settings
///     are read again before every round.
/// </summary>
internal sealed class CandleSyncBackgroundService(
    IServiceScopeFactory scopes,
    CandleLoadQueue queue,
    IOptionsMonitor<CandleSyncSettings> settings,
    IOptionsMonitor<FullHistorySettings> fullHistory,
    TimeProvider time,
    ILogger<CandleSyncBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Each exchange loads on its own, at the pace of its limits.
        return Task.WhenAll(LoadAsync(Exchange.Bybit, stoppingToken), LoadAsync(Exchange.TInvest, stoppingToken));
    }

    // Rounds follow one another while pages are left; a caught-up or disabled exchange waits IdleSeconds.
    private async Task LoadAsync(Exchange exchange, CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        CandleSyncSettings sync = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool hasMorePages = IsEnabled(exchange, sync) && await LoadRoundAsync(exchange, sync, stoppingToken);

            if (!hasMorePages)
            {
                await Task.Delay(TimeSpan.FromSeconds(sync.IdleSeconds), time, stoppingToken);
            }

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // A round: first one page of every range reads asked for, then one page of the history of every listed instrument of the exchange;
    // whether any of them has more to load. An exchange that is unavailable stops the round: every other instrument would fail the same
    // way after retries of its own, so the exchange is tried again after the idle wait; the pages already asked for at that moment end
    // as they do.
    private async Task<bool> LoadRoundAsync(Exchange exchange, CandleSyncSettings sync, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(exchange, logger, stoppingToken);

        try
        {
            bool requestedLeft = await LoadRequestedAsync(exchange, instruments, stoppingToken);
            FullHistorySettings whole = fullHistory.CurrentValue;
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset historyFrom = now.AddDays(-sync.HistoryDays);
            var historyLeft = 0;
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

                    var history = new MinuteRange(MinuteRange.StartOfMinute(whole.HistoryFrom(instrument, historyFrom)), MinuteRange.StartOfMinute(now));

                    try
                    {
                        if (await LoadPageAsync(exchange, instrument.Id, history, whole.Covers(instrument), token) == LoadCandlesResult.MoreLeft)
                        {
                            Interlocked.Exchange(ref historyLeft, value: 1);
                        }
                    }
                    catch (TransientExchangeException exception) when (!token.IsCancellationRequested)
                    {
                        if (Interlocked.Exchange(ref unavailable, value: 1) == 0)
                        {
                            LogUnavailable(exchange, exception);
                        }
                    }
                });

            return unavailable == 0 && (requestedLeft || historyLeft != 0);
        }
        catch (TransientExchangeException exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogUnavailable(exchange, exception);

            return false;
        }
    }

    private void LogUnavailable(Exchange exchange, TransientExchangeException exception)
    {
        logger.LogError(exception, "{Exchange} is unavailable; the loading of its candles goes on after the idle wait.", exchange);
    }

    // One page of every range reads asked for of an instrument of the exchange, listed or not. A range leaves the queue once nothing of it
    // is left to load now: it is loaded, or its page failed and its reader asks again.
    private async Task<bool> LoadRequestedAsync(Exchange exchange, IReadOnlyCollection<InstrumentInternal> instruments, CancellationToken stoppingToken)
    {
        HashSet<Guid> ofExchange = [.. instruments.Select(instrument => instrument.Id)];
        var hasMorePages = false;

        foreach (CandleLoadRequest request in queue.Pending().Where(request => ofExchange.Contains(request.InstrumentId)))
        {
            if (await LoadPageAsync(exchange, request.InstrumentId, request.Range, wholeHistory: false, stoppingToken) == LoadCandlesResult.MoreLeft)
            {
                hasMorePages = true;
            }
            else
            {
                queue.Remove(request);
            }
        }

        return hasMorePages;
    }

    // One page of one instrument of the range. A pause of the exchange is waited out here. A page that fails for a reason of its own is
    // logged and counts as nothing left for now: its history is tried again in the next round.
    private async Task<LoadCandlesResult> LoadPageAsync(
        Exchange exchange,
        Guid instrumentId,
        MinuteRange range,
        bool wholeHistory,
        CancellationToken stoppingToken)
    {
        try
        {
            return await scopes.SendAsync(new LoadCandlesCommand(instrumentId, range, time.GetUtcNow(), wholeHistory), stoppingToken);
        }
        catch (ExchangePausedException exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("{Exchange} paused the loading of candles until {Until:O}: {Reason}", exchange, exception.Until, exception.Message);
            await WaitUntilAsync(exception.Until, stoppingToken);

            return LoadCandlesResult.MoreLeft;
        }
        catch (Exception exception) when (exception is not TransientExchangeException && !stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Loading the candles of instrument {InstrumentId} of {Exchange} failed.", instrumentId, exchange);

            return LoadCandlesResult.NothingLeft;
        }
    }

    private static bool IsEnabled(Exchange exchange, CandleSyncSettings sync)
    {
        return exchange switch
        {
            Exchange.Bybit => sync.Bybit.Enabled,
            Exchange.TInvest => sync.TInvest.Enabled,
            _ => false
        };
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