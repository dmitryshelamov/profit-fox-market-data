using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the history of open interest of every listed linear contract of Bybit, back to
///     <see cref="OpenInterestSyncSettings.HistoryDays" /> or, of those <see cref="FullHistorySettings" /> name, to their launch, and the steps the collection missed, while
///     <see cref="OpenInterestSyncSettings" /> enable Bybit. It goes in rounds (<see cref="Loading" />),
///     <see cref="OpenInterestSyncSettings.Parallelism" /> contracts at a time, while pages are left; then it waits
///     <see cref="OpenInterestSyncSettings.IdleSeconds" /> and looks again. A round in which the collection missed nothing asks Bybit
///     nothing. The settings are read again before every round.
/// </summary>
internal sealed class OpenInterestSyncBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<OpenInterestSyncSettings> settings,
    IOptionsMonitor<FullHistorySettings> fullHistory,
    TimeProvider time,
    ILogger<OpenInterestSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        OpenInterestSyncSettings sync = settings.CurrentValue;
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
    private async Task<LoadingRoundResult> LoadRoundAsync(Loading loading, OpenInterestSyncSettings sync, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);
        FullHistorySettings whole = fullHistory.CurrentValue;
        DateTimeOffset historyFrom = time.GetUtcNow().AddDays(-sync.HistoryDays);

        return await loading.RunRoundAsync(
            instruments.Where(HasOpenInterest),
            sync.Parallelism,
            async (instrument, token) =>
                await scopes.SendAsync(new LoadOpenInterestCommand(instrument.Id, whole.HistoryFrom(instrument, historyFrom), time.GetUtcNow()), token) == LoadOpenInterestResult.MoreLeft,
            stoppingToken);
    }

    private Loading NewLoading()
    {
        return new Loading("open interest", time, logger);
    }

    // A linear contract has open interest, perpetual or dated; a spot pair has none.
    private static bool HasOpenInterest(InstrumentInternal instrument)
    {
        return instrument.DelistedAt is null && instrument.Listing is BybitListedInstrumentInternal { Category: BybitCategory.Linear };
    }
}