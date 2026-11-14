using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Exchanges;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Loads the candles of every listed instrument of the exchanges <see cref="CandleSyncSettings" /> enables, back to
///     <see cref="CandleSyncSettings.HistoryDays" />, and before them the windows reads asked for and could not load in time
///     (<see cref="CandleLoadQueue" />). A round loads one page of every window and every instrument in turn, the newest it misses first,
///     so fresh minutes come before old history and every instrument moves on at the same pace. Rounds follow one another while pages
///     are left; an exchange with none left waits <see cref="CandleSyncSettings.IdleSeconds" />. The settings are read again before every
///     round.
/// </summary>
internal sealed class CandleSyncBackgroundService(
    IServiceScopeFactory scopes,
    CandleLoadQueue queue,
    IOptionsMonitor<CandleSyncSettings> settings,
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
            bool hasMorePages = IsEnabled(exchange, sync) && await LoadRoundAsync(exchange, sync.HistoryDays, stoppingToken);

            if (!hasMorePages)
            {
                await Task.Delay(TimeSpan.FromSeconds(sync.IdleSeconds), time, stoppingToken);
            }

            sync = ReadSettings(sync);
        }
    }

    // A round: first one page of every window reads asked for, then one page of the history of every listed instrument of the exchange;
    // whether any of them has more to load.
    private async Task<bool> LoadRoundAsync(Exchange exchange, int historyDays, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await InstrumentsAsync(exchange, stoppingToken);
        bool hasMorePages = await LoadRequestedAsync(exchange, instruments, stoppingToken);
        MinuteRange history = History(historyDays);

        foreach (InstrumentInternal instrument in instruments.Where(instrument => instrument.DelistedAt is null))
        {
            hasMorePages |= await LoadPageAsync(exchange, instrument.Id, history, stoppingToken) == Page.MoreLeft;
        }

        return hasMorePages;
    }

    // One page of every window reads asked for of an instrument of the exchange, listed or not; a window leaves the queue once nothing of
    // it is left, and one that failed stays for the next round.
    private async Task<bool> LoadRequestedAsync(Exchange exchange, IReadOnlyCollection<InstrumentInternal> instruments, CancellationToken stoppingToken)
    {
        HashSet<Guid> ofExchange = [.. instruments.Select(instrument => instrument.Id)];
        var hasMorePages = false;

        foreach (CandleLoadRequest request in queue.Pending().Where(request => ofExchange.Contains(request.InstrumentId)))
        {
            Page page = await LoadPageAsync(exchange, request.InstrumentId, request.Range, stoppingToken);

            if (page == Page.NoneLeft)
            {
                queue.Complete(request);
            }

            hasMorePages |= page == Page.MoreLeft;
        }

        return hasMorePages;
    }

    // One page of one instrument of the range. A pause of the exchange is waited out here, outside the transaction of the command; any
    // other failure is logged and the page is tried again in the next round.
    private async Task<Page> LoadPageAsync(Exchange exchange, Guid instrumentId, MinuteRange range, CancellationToken stoppingToken)
    {
        try
        {
            return await SendAsync(new LoadCandlesCommand(instrumentId, range, time.GetUtcNow()), stoppingToken) ? Page.MoreLeft : Page.NoneLeft;
        }
        catch (ExchangePausedException exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("{Exchange} paused the loading of candles until {Until:O}: {Reason}", exchange, exception.Until, exception.Message);
            await WaitUntilAsync(exception.Until, stoppingToken);

            return Page.MoreLeft;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Loading the candles of instrument {InstrumentId} of {Exchange} failed.", instrumentId, exchange);

            return Page.Failed;
        }
    }

    // The instruments of the exchange in the catalog, listed or delisted; none when the catalog cannot be read, which is logged.
    private async Task<IReadOnlyCollection<InstrumentInternal>> InstrumentsAsync(Exchange exchange, CancellationToken stoppingToken)
    {
        try
        {
            return exchange switch
            {
                Exchange.Bybit => await SendAsync(new GetBybitInstrumentsQuery(), stoppingToken),
                Exchange.TInvest => await SendAsync(new GetTInvestSharesQuery(), stoppingToken),
                _ => throw new NotSupportedException($"Candles of {exchange} are not loaded.")
            };
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading the instruments of {Exchange} to load candles failed.", exchange);

            return [];
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

    // The whole minutes from historyDays back to now.
    private MinuteRange History(int historyDays)
    {
        DateTimeOffset now = time.GetUtcNow();

        return new MinuteRange(StartOfMinute(now.AddDays(-historyDays)), StartOfMinute(now));
    }

    private async Task WaitUntilAsync(DateTimeOffset until, CancellationToken stoppingToken)
    {
        TimeSpan wait = until - time.GetUtcNow();

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, stoppingToken);
        }
    }

    // A broken change is logged, and the last valid settings stay until it is fixed.
    private CandleSyncSettings ReadSettings(CandleSyncSettings lastValid)
    {
        try
        {
            return settings.CurrentValue;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
        {
            logger.LogError(exception, "The CandleSync settings are broken; the loading goes on with the last valid ones.");

            return lastValid;
        }
    }

    // Each request has a scope of its own, so it has a connection of its own and a command a transaction of its own.
    private async Task<T> SendAsync<T>(IRequest<T> request, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, stoppingToken);
    }

    private static DateTimeOffset StartOfMinute(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcTicks - time.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    private enum Exchange
    {
        Bybit,
        TInvest
    }

    // What a page left of its range: more to load, nothing, or a failure that leaves the range as it was.
    private enum Page
    {
        MoreLeft,
        NoneLeft,
        Failed
    }
}