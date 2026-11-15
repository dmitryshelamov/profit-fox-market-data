using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the long/short ratio of every listed linear contract of Bybit quoted in USDT, back to
///     <see cref="LongShortRatioSyncSettings.HistoryDays" /> and on from where it was loaded to, while
///     <see cref="LongShortRatioSyncSettings" /> enable Bybit. Bybit has no other source of the ratio than its history, a contract a
///     request, so the newest points come with the rounds of this loading and not from a snapshot of the market. It goes in rounds
///     (<see cref="Loading" />), <see cref="LongShortRatioSyncSettings.Parallelism" /> contracts at a time, while pages are left; then
///     it waits <see cref="LongShortRatioSyncSettings.IdleSeconds" /> and looks again. The settings are read again before every round.
/// </summary>
internal sealed class LongShortRatioSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<LongShortRatioSyncSettings> settings,
    TimeProvider time,
    ILogger<LongShortRatioSyncBackgroundService> logger) : BackgroundService
{
    // The asset the contracts Bybit keeps the ratio of are quoted in.
    private const string QuoteAsset = "USDT";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        LongShortRatioSyncSettings sync = settings.CurrentValue;
        Loading loading = NewLoading();

        while (!stoppingToken.IsCancellationRequested)
        {
            bool hasMorePages = sync.Bybit.Enabled && await LoadRoundAsync(loading, sync, stoppingToken) == LoadingRoundResult.MoreLeft;

            if (!hasMorePages)
            {
                await Task.Delay(TimeSpan.FromSeconds(sync.IdleSeconds), time, stoppingToken);

                // A contract whose page failed, and Bybit when it was unavailable, are tried again after the wait.
                loading = NewLoading();
            }

            sync = settings.CurrentOrLastValid(sync, logger);
        }
    }

    // A round asks every page as of now: the steps told since the round before are looked for with the history.
    private async Task<LoadingRoundResult> LoadRoundAsync(Loading loading, LongShortRatioSyncSettings sync, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);
        DateTimeOffset from = time.GetUtcNow().AddDays(-sync.HistoryDays);

        return await loading.RunRoundAsync(
            instruments.Where(HasLongShortRatio),
            sync.Parallelism,
            async (instrument, token) =>
                await scopes.SendAsync(new LoadLongShortRatioCommand(instrument.Id, from, time.GetUtcNow()), token) == LoadLongShortRatioResult.MoreLeft,
            stoppingToken);
    }

    private Loading NewLoading()
    {
        return new Loading("the long/short ratio", time, logger);
    }

    // Bybit keeps the ratio of a linear contract quoted in USDT, perpetual or dated; of one quoted in USDC and of a spot pair it has none.
    private static bool HasLongShortRatio(InstrumentInternal instrument)
    {
        return instrument.DelistedAt is null && instrument.Listing is BybitListedInstrumentInternal { Category: BybitCategory.Linear, QuoteAsset: QuoteAsset };
    }
}