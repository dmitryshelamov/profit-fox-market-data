using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Saves the minute candles of every listed instrument of Bybit as they close, each with its buy volume, while
///     <see cref="CandleStreamSettings" /> enables Bybit. It subscribes to the listed instruments of the catalog and reads the catalog
///     again every <see cref="CandleStreamSettings.RefreshSeconds" /> for the ones listed since. A delisted instrument stays subscribed
///     until the app restarts: Bybit tells nothing more of it. The closed candles wait in memory and are saved together,
///     <see cref="CandleStreamSettings.BatchSeconds" /> after the first of them came, so a minute of the whole market is one save. A
///     minute the stream did not tell, as during a downtime, is left to <see cref="CandleSyncBackgroundService" />.
/// </summary>
internal sealed class CandleStreamBackgroundService(
    IServiceScopeFactory scopes,
    IBybitCandleStreamClient bybit,
    IOptionsMonitor<CandleStreamSettings> settings,
    TimeProvider time,
    ILogger<CandleStreamBackgroundService> logger) : BackgroundService
{
    // About seven minutes of the candles of the whole market. When storage falls further behind, the oldest waiting candles are dropped:
    // their minutes stay uncovered and the loading of candles takes them.
    private const int WaitingCapacity = 10_000;

    private readonly Channel<ClosedCandle> _closed = Channel.CreateBounded<ClosedCandle>(
        new BoundedChannelOptions(WaitingCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly HashSet<(BybitCategory Category, string Symbol)> _subscribed = [];

    private readonly List<IAsyncDisposable> _subscriptions = [];

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(SubscribeAsync(stoppingToken), SaveAsync(stoppingToken));
    }

    // Subscribes to what the catalog lists and is not subscribed to yet, again and again; the subscriptions end with the service.
    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        CandleStreamSettings stream = settings.CurrentValue;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (stream.Bybit.Enabled)
                {
                    await SubscribeToListedAsync(stoppingToken);
                }
                else
                {
                    await UnsubscribeAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(stream.RefreshSeconds), time, stoppingToken);

                stream = settings.CurrentOrLastValid(stream, logger);
            }
        }
        finally
        {
            await UnsubscribeAsync();
        }
    }

    // One subscription a category for its listed instruments not subscribed to yet. The categories subscribe at the same time: each has
    // a stream of its own at Bybit, and the hundreds of requests of one would hold back the other.
    private async Task SubscribeToListedAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);

        // The ids of the instruments to subscribe to by their symbols, a category at a time.
        IEnumerable<IGrouping<BybitCategory, KeyValuePair<string, Guid>>> categories = instruments
            .Where(instrument => instrument.DelistedAt is null)
            .Select(instrument => (instrument.Id, Listing: (BybitListedInstrumentInternal)instrument.Listing))
            .Where(instrument => !_subscribed.Contains((instrument.Listing.Category, instrument.Listing.Symbol)))
            .GroupBy(instrument => instrument.Listing.Category, instrument => KeyValuePair.Create(instrument.Listing.Symbol, instrument.Id));

        CategorySubscription?[] taken = await Task.WhenAll(
            categories.Select(category => SubscribeToCategoryAsync(category.Key, new Dictionary<string, Guid>(category), stoppingToken)));

        foreach (CategorySubscription subscription in taken.OfType<CategorySubscription>())
        {
            _subscriptions.Add(subscription.Subscription);
            _subscribed.UnionWith(subscription.Symbols.Select(symbol => (subscription.Category, symbol)));
        }
    }

    // The subscription to the instruments of a category; null when Bybit did not take it, which is logged: it is tried again after the
    // wait.
    private async Task<CategorySubscription?> SubscribeToCategoryAsync(BybitCategory category, Dictionary<string, Guid> ids, CancellationToken stoppingToken)
    {
        try
        {
            IAsyncDisposable subscription = await bybit.SubscribeAsync(category, ids.Keys, (symbol, candle) => Closed(ids[symbol], candle), stoppingToken);
            logger.LogInformation("Subscribed to the closed candles of {Count} {Category} instruments of Bybit.", ids.Count, category);

            return new CategorySubscription(category, ids.Keys, subscription);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Subscribing to the closed candles of {Count} {Category} instruments of Bybit failed; it is tried again later.",
                ids.Count,
                category);

            return null;
        }
    }

    private async Task UnsubscribeAsync()
    {
        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions.Clear();
        _subscribed.Clear();
    }

    // Called on the threads of the stream: the candle only joins the ones waiting to be saved.
    private void Closed(Guid instrumentId, Candle candle)
    {
        _closed.Writer.TryWrite(new ClosedCandle(Exchange.Bybit, instrumentId, candle));
    }

    // The first candle that waits opens a batch; BatchSeconds later all that wait by then are saved together, in a scope of their own.
    // A candle that comes after that opens the next batch. A batch that fails to save is logged and its minutes are left to the loading
    // of candles.
    private async Task SaveAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        CandleStreamSettings stream = settings.CurrentValue;

        while (await _closed.Reader.WaitToReadAsync(stoppingToken))
        {
            await Task.Delay(TimeSpan.FromSeconds(stream.BatchSeconds), time, stoppingToken);

            var batch = new List<ClosedCandle>();

            while (_closed.Reader.TryRead(out ClosedCandle? closed))
            {
                batch.Add(closed);
            }

            try
            {
                SaveClosedCandlesResult result = await scopes.SendAsync(new SaveClosedCandlesCommand(batch), stoppingToken);

                logger.LogInformation(
                    "Saved {Saved} of {Told} closed candles, {WithBuyVolume} told with a buy volume.",
                    result.Saved,
                    batch.Count,
                    batch.Count(closed => closed.Candle.BuyVolume is not null));
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Saving {Told} closed candles failed.", batch.Count);
            }

            stream = settings.CurrentOrLastValid(stream, logger);
        }
    }

    // A subscription Bybit took: the symbols of a category it tells the closed candles of.
    private sealed record CategorySubscription(BybitCategory Category, IReadOnlyCollection<string> Symbols, IAsyncDisposable Subscription);
}