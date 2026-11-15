using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     One loading of the history of an exchange by a background service, round after round: what the loadings do the same way, so a
///     service tells only which instruments it loads, when, and with which command. An instrument whose page failed for a reason of
///     its own is left alone for the rest of the loading: asked again every round, it would fail the same way hundreds of times. A
///     service starts a new loading when it is time to try such instruments again.
/// </summary>
internal sealed class Loading(string what, TimeProvider time, ILogger logger)
{
    private readonly ConcurrentDictionary<Guid, bool> _failed = new ConcurrentDictionary<Guid, bool>();

    /// <summary>
    ///     A round: one page of each of the instruments, <paramref name="parallelism" /> of them at a time, so every instrument moves
    ///     on at the same pace; <paramref name="loadPage" /> loads the page and tells whether the instrument has more left. A pause
    ///     of the exchange is waited out by the page that met it, and its instrument is asked again in the next round. The exchange
    ///     being unavailable stops the round: every other instrument would fail the same way after retries of its own.
    /// </summary>
    public async Task<LoadingRoundResult> RunRoundAsync(
        IEnumerable<InstrumentInternal> instruments,
        int parallelism,
        Func<InstrumentInternal, CancellationToken, Task<bool>> loadPage,
        CancellationToken stoppingToken)
    {
        var hasMorePages = 0;
        var unavailable = 0;

        await Parallel.ForEachAsync(
            instruments.Where(instrument => !_failed.ContainsKey(instrument.Id)),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = stoppingToken },
            async (instrument, token) =>
            {
                if (Volatile.Read(ref unavailable) != 0)
                {
                    return;
                }

                try
                {
                    if (await loadPage(instrument, token))
                    {
                        Interlocked.Exchange(ref hasMorePages, value: 1);
                    }
                }
                catch (ExchangePausedException exception) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning("The exchange paused the loading of {What} until {Until:O}: {Reason}", what, exception.Until, exception.Message);
                    await time.DelayUntilAsync(exception.Until, token);
                    Interlocked.Exchange(ref hasMorePages, value: 1);
                }
                catch (TransientExchangeException exception) when (!token.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref unavailable, value: 1) == 0)
                    {
                        logger.LogError(exception, "The exchange is unavailable; the loading of {What} stops for now.", what);
                    }
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    _failed[instrument.Id] = true;
                    logger.LogError(
                        exception,
                        "Loading the {What} of instrument {InstrumentId} failed; it is left until the next loading.",
                        what,
                        instrument.Id);
                }
            });

        if (unavailable != 0)
        {
            return LoadingRoundResult.Unavailable;
        }

        return hasMorePages != 0 ? LoadingRoundResult.MoreLeft : LoadingRoundResult.NothingLeft;
    }
}