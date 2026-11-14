using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Collects the open interest and the funding rates of the linear market of Bybit at every minute boundary in UTC, while
///     <see cref="TickerCollectionSettings" /> enable Bybit: one command a minute, with the time of the boundary. A snapshot not taken
///     within <see cref="TickerCollectionSettings.DeadlineSeconds" /> of its boundary is given up, and so is one Bybit holds or fails:
///     that minute is skipped for good, nothing is filled in later, and the service waits for the next boundary. The settings are read
///     again before every snapshot.
/// </summary>
internal sealed class TickerCollectionBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<TickerCollectionSettings> settings,
    TimeProvider time,
    ILogger<TickerCollectionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        TickerCollectionSettings collection = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            // The boundary is counted from the clock every time, so the snapshots do not drift and a boundary that passed while a
            // snapshot was taken is skipped, not caught up with.
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset boundary = MinuteRange.StartOfMinute(now).AddMinutes(1);

            await Task.Delay(boundary - now, time, stoppingToken);

            collection = settings.CurrentOrLastValid(collection, logger);

            if (collection.Bybit.Enabled)
            {
                await CollectAsync(boundary, TimeSpan.FromSeconds(collection.DeadlineSeconds), stoppingToken);
            }
        }
    }

    // The snapshot of one boundary, in a scope of its own. Whatever keeps it from being saved in time is logged and skips the minute; an
    // escaping exception would stop the host.
    private async Task CollectAsync(DateTimeOffset boundary, TimeSpan deadline, CancellationToken stoppingToken)
    {
        // The deadline runs on the clock of the app, as every wait of the service does.
        using var timeout = new CancellationTokenSource(deadline, time);
        using var timeoutOrStopping = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, stoppingToken);

        try
        {
            CollectBybitTickersResult collected = await scopes.SendAsync(new CollectBybitTickersCommand(boundary), timeoutOrStopping.Token);
            logger.LogInformation(
                "Collected {OpenInterestPoints} points of open interest and {FundingRatePoints} funding rates of Bybit for {Boundary:O}.",
                collected.OpenInterestPoints,
                collected.FundingRatePoints,
                boundary);
        }
        catch (Exception exception) when (timeout.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "The tickers of Bybit for {Boundary:O} were not collected within {Deadline} s; the minute is skipped.",
                boundary,
                deadline.TotalSeconds);
        }
        catch (ExchangePausedException exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Bybit holds the requests until {Until:O}; the tickers for {Boundary:O} are skipped: {Reason}",
                exception.Until,
                boundary,
                exception.Message);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Collecting the tickers of Bybit for {Boundary:O} failed; the minute is skipped.", boundary);
        }
    }
}