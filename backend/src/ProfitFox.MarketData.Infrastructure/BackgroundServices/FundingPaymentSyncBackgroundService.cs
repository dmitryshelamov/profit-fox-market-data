using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the funding payments of every listed perpetual contract of Bybit, back to
///     <see cref="FundingPaymentSyncSettings.HistoryDays" /> or, of those <see cref="FullHistorySettings" /> name, to their launch, while <see cref="FundingPaymentSyncSettings" /> enable Bybit. A load
///     starts when the app starts and then half a minute after every whole hour, when the payments of that hour are told. It goes in
///     rounds (<see cref="Loading" />), <see cref="FundingPaymentSyncSettings.Parallelism" /> contracts at a time, while history is
///     left. A load still going at a whole hour is not cut short: the next one starts after the first whole hour that follows it and
///     takes what was paid meanwhile. The settings are read again before every load.
/// </summary>
internal sealed class FundingPaymentSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<FundingPaymentSyncSettings> settings,
    IOptionsMonitor<FullHistorySettings> fullHistory,
    TimeProvider time,
    ILogger<FundingPaymentSyncBackgroundService> logger) : BackgroundService
{
    // Bybit pays funding on whole hours and tells a payment a few seconds later. The load must start later after the hour than a
    // command takes a payment to be told (the Publishing of LoadFundingPaymentsCommandHandler), or the payment of the hour would be left
    // to the load of the next one.
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
    // rounds after it only history, with no request for the seconds that passed in between. A contract whose page failed, and Bybit
    // when it is unavailable, are tried again by the next load.
    private async Task LoadAsync(FundingPaymentSyncSettings sync, CancellationToken stoppingToken)
    {
        DateTimeOffset requestedAt = time.GetUtcNow();
        FullHistorySettings whole = fullHistory.CurrentValue;
        DateTimeOffset historyFrom = requestedAt.AddDays(-sync.HistoryDays);
        var loading = new Loading("funding payments", time, logger);
        LoadingRoundResult round;

        do
        {
            IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);

            round = await loading.RunRoundAsync(
                instruments.Where(PaysFunding),
                sync.Parallelism,
                async (instrument, token) =>
                    await scopes.SendAsync(new LoadFundingPaymentsCommand(instrument.Id, whole.HistoryFrom(instrument, historyFrom), requestedAt), token) == LoadFundingPaymentsResult.MoreLeft,
                stoppingToken);
        }
        while (round == LoadingRoundResult.MoreLeft && !stoppingToken.IsCancellationRequested);

        if (round == LoadingRoundResult.NothingLeft)
        {
            logger.LogInformation("Loaded the funding payments of Bybit paid by {RequestedAt:O}.", requestedAt);
        }
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
}