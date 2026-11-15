using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the funding payments of every listed perpetual contract of Bybit, back to
///     <see cref="FundingPaymentSyncSettings.HistoryDays" />, while <see cref="FundingPaymentSyncSettings" /> enable Bybit: once when the
///     app starts and then half a minute after every whole hour, when the payments of that hour are told. A load goes in rounds of one
///     page for every contract, <see cref="FundingPaymentSyncSettings.Parallelism" /> contracts at a time, so what was paid since the
///     last load comes before old history and every contract moves on at the same pace; rounds follow one another while history is
///     left. A load that is still going at a whole hour is not cut short: the
///     next one starts after the first whole hour that follows it and takes what was paid meanwhile. The settings are read again before
///     every load.
/// </summary>
internal sealed class FundingPaymentSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<FundingPaymentSyncSettings> settings,
    TimeProvider time,
    ILogger<FundingPaymentSyncBackgroundService> logger) : BackgroundService
{
    // Bybit pays funding on whole hours and tells a payment a few seconds later.
    private static readonly TimeSpan AfterTheHour = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        FundingPaymentSyncSettings sync = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (sync.Bybit.Enabled)
            {
                await LoadAsync(sync, stoppingToken);
            }

            await Task.Delay(UntilNextLoad(time.GetUtcNow()), time, stoppingToken);

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // Every page of a load is asked as of the moment the load began: its first round takes what was paid since the last load, and the
    // rounds after it only history, with no request for the seconds that passed in between.
    private async Task LoadAsync(FundingPaymentSyncSettings sync, CancellationToken stoppingToken)
    {
        DateTimeOffset requestedAt = time.GetUtcNow();
        DateTimeOffset from = requestedAt.AddDays(-sync.HistoryDays);
        bool hasMorePages;

        do
        {
            hasMorePages = await LoadRoundAsync(from, requestedAt, sync.Parallelism, stoppingToken);
        }
        while (hasMorePages && !stoppingToken.IsCancellationRequested);

        logger.LogInformation("Loaded the funding payments of Bybit paid by {RequestedAt:O}.", requestedAt);
    }

    // A round: one page of every listed perpetual contract; whether any of them has more to load. A pause of Bybit is waited out by the
    // page that met it, and its contract is asked again in the next round. A page that fails for a reason of its own is logged and its
    // contract is tried again by the next load. Bybit being unavailable stops the load: every other contract would fail the same way
    // after retries of its own, so Bybit is tried again after the next whole hour.
    private async Task<bool> LoadRoundAsync(DateTimeOffset from, DateTimeOffset requestedAt, int parallelism, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);
        var hasMorePages = 0;
        var unavailable = 0;

        await Parallel.ForEachAsync(
            instruments.Where(PaysFunding),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = stoppingToken },
            async (instrument, token) =>
            {
                if (Volatile.Read(ref unavailable) != 0)
                {
                    return;
                }

                try
                {
                    LoadFundingPaymentsResult page = await scopes.SendAsync(new LoadFundingPaymentsCommand(instrument.Id, from, requestedAt), token);

                    if (page == LoadFundingPaymentsResult.MoreLeft)
                    {
                        Interlocked.Exchange(ref hasMorePages, value: 1);
                    }
                }
                catch (ExchangePausedException exception) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning("Bybit paused the loading of funding payments until {Until:O}: {Reason}", exception.Until, exception.Message);
                    await WaitUntilAsync(exception.Until, token);
                    Interlocked.Exchange(ref hasMorePages, value: 1);
                }
                catch (TransientExchangeException exception) when (!token.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref unavailable, value: 1) == 0)
                    {
                        logger.LogError(exception, "Bybit is unavailable; the loading of its funding payments goes on after the next whole hour.");
                    }
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    logger.LogError(exception, "Loading the funding payments of instrument {InstrumentId} of Bybit failed.", instrument.Id);
                }
            });

        return hasMorePages != 0 && unavailable == 0;
    }

    // Only a perpetual contract pays funding: a spot pair and a dated futures contract have no payments to ask for.
    private static bool PaysFunding(InstrumentInternal instrument)
    {
        return instrument.DelistedAt is null
            && instrument.Listing is BybitListedInstrumentInternal { ContractType: BybitContractType.LinearPerpetual };
    }

    // The time left until half a minute after a whole hour, the coming one when this hour's moment has passed.
    private static TimeSpan UntilNextLoad(DateTimeOffset now)
    {
        DateTimeOffset afterThisHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, minute: 0, second: 0, TimeSpan.Zero) + AfterTheHour;

        return (afterThisHour > now ? afterThisHour : afterThisHour.AddHours(1)) - now;
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