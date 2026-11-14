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
///     Saves the minute candles of every listed instrument as they close, each with its buy volume, of the exchanges
///     <see cref="CandleStreamSettings" /> enables: Bybit and T-Invest. It subscribes to the listed instruments of the catalog and reads
///     the catalog again every <see cref="CandleStreamSettings.RefreshSeconds" /> for the ones listed since. A delisted instrument stays
///     subscribed until the app restarts: its exchange tells nothing more of it. The closed candles wait in memory and are saved together,
///     <see cref="CandleStreamSettings.BatchSeconds" /> after the first of them came, so a minute of the whole market is one save. The
///     forming candles of the minute that goes on are not saved: they go to the feed of live candles as they come. A minute the stream
///     did not tell, as during a downtime, is left to <see cref="CandleSyncBackgroundService" />.
/// </summary>
internal sealed class CandleStreamBackgroundService(
    IServiceScopeFactory scopes,
    IBybitCandleStreamClient bybit,
    ITInvestCandleStreamClient tinvest,
    ILiveCandleFeed feed,
    IOptionsMonitor<CandleStreamSettings> settings,
    TimeProvider time,
    ILogger<CandleStreamBackgroundService> logger) : BackgroundService
{
    // About seven minutes of the candles of the whole market. When storage falls further behind, the oldest waiting candles are dropped:
    // their minutes stay uncovered and the loading of candles takes them.
    private const int WaitingCapacity = 10_000;

    private readonly Channel<ClosedCandle> _closed = Channel.CreateBounded<ClosedCandle>(
        new BoundedChannelOptions(WaitingCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // The instruments of Bybit subscribed to, and their subscriptions.
    private readonly HashSet<(BybitCategory Category, string Symbol)> _bybitSymbols = [];

    private readonly List<IAsyncDisposable> _bybitSubscriptions = [];

    // The uids of the shares of T-Invest subscribed to, and their subscriptions.
    private readonly HashSet<string> _tinvestShares = [];

    private readonly List<IAsyncDisposable> _tinvestSubscriptions = [];

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
                // Each exchange on its own: the hundreds of requests of Bybit do not hold back T-Invest.
                await Task.WhenAll(
                    stream.Bybit.Enabled ? SubscribeToBybitAsync(stoppingToken) : UnsubscribeAsync(_bybitSubscriptions, _bybitSymbols),
                    stream.TInvest.Enabled ? SubscribeToTInvestAsync(stoppingToken) : UnsubscribeAsync(_tinvestSubscriptions, _tinvestShares));

                await Task.Delay(TimeSpan.FromSeconds(stream.RefreshSeconds), time, stoppingToken);

                stream = settings.CurrentOrLastValid(stream, logger);
            }
        }
        finally
        {
            await UnsubscribeAsync(_bybitSubscriptions, _bybitSymbols);
            await UnsubscribeAsync(_tinvestSubscriptions, _tinvestShares);
        }
    }

    // One subscription a category for its listed instruments not subscribed to yet. The categories subscribe at the same time: each has
    // a stream of its own at Bybit, and the hundreds of requests of one would hold back the other.
    private async Task SubscribeToBybitAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);

        // The ids of the instruments to subscribe to by their symbols, a category at a time.
        IEnumerable<IGrouping<BybitCategory, KeyValuePair<string, Guid>>> categories = instruments
            .Where(instrument => instrument.DelistedAt is null)
            .Select(instrument => (instrument.Id, Listing: (BybitListedInstrumentInternal)instrument.Listing))
            .Where(instrument => !_bybitSymbols.Contains((instrument.Listing.Category, instrument.Listing.Symbol)))
            .GroupBy(instrument => instrument.Listing.Category, instrument => KeyValuePair.Create(instrument.Listing.Symbol, instrument.Id));

        CategorySubscription?[] taken = await Task.WhenAll(
            categories.Select(category => SubscribeToBybitCategoryAsync(category.Key, new Dictionary<string, Guid>(category), stoppingToken)));

        foreach (CategorySubscription subscription in taken.OfType<CategorySubscription>())
        {
            _bybitSubscriptions.Add(subscription.Subscription);
            _bybitSymbols.UnionWith(subscription.Symbols.Select(symbol => (subscription.Category, symbol)));
        }
    }

    // The subscription to the instruments of a category; null when Bybit did not take it, which is logged: it is tried again after the
    // wait.
    private async Task<CategorySubscription?> SubscribeToBybitCategoryAsync(BybitCategory category, Dictionary<string, Guid> ids, CancellationToken stoppingToken)
    {
        try
        {
            IAsyncDisposable subscription = await bybit.SubscribeAsync(
                category,
                ids.Keys,
                (symbol, candle) => Closed(Exchange.Bybit, ids[symbol], candle),
                (symbol, candle) => feed.Publish([new LiveCandle(Exchange.Bybit, ids[symbol], candle, Closed: false)]),
                stoppingToken);
            logger.LogInformation("Subscribed to the candles of {Count} {Category} instruments of Bybit.", ids.Count, category);

            return new CategorySubscription(category, ids.Keys, subscription);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Subscribing to the candles of {Count} {Category} instruments of Bybit failed; it is tried again later.",
                ids.Count,
                category);

            return null;
        }
    }

    // One subscription for the listed shares of T-Invest not subscribed to yet: all of them at the first pass, later only the ones
    // listed since. The stream of T-Invest opens and reopens by itself, so only a missing token fails here.
    private async Task SubscribeToTInvestAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.TInvest, logger, stoppingToken);

        (Guid Id, TInvestListedShareInternal Listing)[] listed =
        [
            .. instruments
                .Where(instrument => instrument.DelistedAt is null)
                .Select(instrument => (instrument.Id, Listing: (TInvestListedShareInternal)instrument.Listing))
                .Where(share => !_tinvestShares.Contains(share.Listing.Uid))
        ];

        if (listed.Length == 0)
        {
            return;
        }

        Dictionary<string, Guid> ids = listed.ToDictionary(share => share.Listing.Uid, share => share.Id);

        try
        {
            _tinvestSubscriptions.Add(
                await tinvest.SubscribeAsync(
                    listed.ToDictionary(share => share.Listing.Uid, share => share.Listing.Rules.QuantityStep),
                    (uid, candle) => Closed(Exchange.TInvest, ids[uid], candle),
                    (uid, candle) => feed.Publish([new LiveCandle(Exchange.TInvest, ids[uid], candle, Closed: false)]),
                    stoppingToken));
            _tinvestShares.UnionWith(ids.Keys);
            logger.LogInformation("Subscribed to the candles of {Count} shares of T-Invest.", ids.Count);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Subscribing to the candles of {Count} shares of T-Invest failed; it is tried again later.", ids.Count);
        }
    }

    // Ends the subscriptions and forgets what they were to, so it is subscribed to anew when its exchange is on again.
    private static async Task UnsubscribeAsync<TSubscribed>(List<IAsyncDisposable> subscriptions, HashSet<TSubscribed> subscribed)
    {
        foreach (IAsyncDisposable subscription in subscriptions)
        {
            await subscription.DisposeAsync();
        }

        subscriptions.Clear();
        subscribed.Clear();
    }

    // Called on the threads of the stream: the candle only joins the ones waiting to be saved.
    private void Closed(Exchange exchange, Guid instrumentId, Candle candle)
    {
        _closed.Writer.TryWrite(new ClosedCandle(exchange, instrumentId, candle));
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

    // A subscription Bybit took: the symbols of a category it tells the candles of.
    private sealed record CategorySubscription(BybitCategory Category, IReadOnlyCollection<string> Symbols, IAsyncDisposable Subscription);
}