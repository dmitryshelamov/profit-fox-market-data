using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The closed minute candles of Bybit from its public streams. A candle is the one of the topic <c>kline.1</c>, taken when Bybit
///     confirms it; its buy volume is summed from the topic <c>publicTrade</c>. The two agree: the trades of a minute add up to the
///     volume of its candle exactly, so a sum that does not is a minute whose trades were not all seen, and its buy volume is not told.
///     A trade of the last closed minute that comes after its candle is still counted, and the candle is told again once they add up.
///     The closed candle is not built from the trades: its open is the close of the minute before, not the first trade. The candle of
///     the minute that goes on is: Bybit pushes one too, but never at the moment the trades were summed, so its volume and the bought
///     part would not agree. It is told once a second for the symbols that traded in that second. CryptoExchange.Net reconnects and
///     subscribes again.
/// </summary>
internal sealed class BybitCandleStreamClient(IBybitSocketClient bybit, TimeProvider time, ILogger<BybitCandleStreamClient> logger)
    : IBybitCandleStreamClient
{
    // The most topics Bybit takes in one subscribe request of spot; linear uses the same, so both lay out alike.
    private const int BatchSize = 10;

    private static readonly TimeSpan FormingPeriod = TimeSpan.FromSeconds(1);

    public async Task<IAsyncDisposable> SubscribeAsync(
        BybitCategory category,
        IReadOnlyCollection<string> symbols,
        Action<string, Candle> closed,
        Action<string, Candle> forming,
        CancellationToken cancellationToken)
    {
        var trades = new BybitMinuteTrades();
        var subscription = new Subscription(time.CreateTimer(_ => TellForming(category, trades, forming), state: null, FormingPeriod, FormingPeriod));

        try
        {
            foreach (string[] batch in symbols.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The trades first, so they are counted by the time the first candle of the batch closes. They are seen from now on,
                // and from the moment the connection is back after a break.
                UpdateSubscription ofTrades = Subscribed(category, await SubscribeToTradesAsync(category, batch, trades, closed));
                trades.Watch(batch, time.GetUtcNow());
                ofTrades.ConnectionRestored += _ => trades.Watch(batch, time.GetUtcNow());
                subscription.Add(ofTrades);
                subscription.Add(Subscribed(category, await SubscribeToKlinesAsync(category, batch, trades, closed)));
            }
        }
        catch
        {
            await subscription.DisposeAsync();

            throw;
        }

        return subscription;
    }

    // Made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
    private Task<WebSocketResult<UpdateSubscription>> SubscribeToTradesAsync(
        BybitCategory category,
        string[] symbols,
        BybitMinuteTrades trades,
        Action<string, Candle> closed)
    {
        return category switch
        {
            BybitCategory.Spot => bybit.V5SpotApi.SubscribeToTradeUpdatesAsync(
                symbols,
                update => Count(category, update.Data, trades, closed),
                CancellationToken.None),
            BybitCategory.Linear => bybit.V5LinearApi.SubscribeToTradeUpdatesAsync(
                symbols,
                update => Count(category, update.Data, trades, closed),
                CancellationToken.None),
            _ => throw new NotSupportedException($"Candles of {category} instruments of Bybit are not streamed.")
        };
    }

    private Task<WebSocketResult<UpdateSubscription>> SubscribeToKlinesAsync(
        BybitCategory category,
        string[] symbols,
        BybitMinuteTrades trades,
        Action<string, Candle> closed)
    {
        return category switch
        {
            BybitCategory.Spot => bybit.V5SpotApi.SubscribeToKlineUpdatesAsync(
                symbols,
                KlineInterval.OneMinute,
                update => Close(category, update, trades, closed),
                CancellationToken.None),
            BybitCategory.Linear => bybit.V5LinearApi.SubscribeToKlineUpdatesAsync(
                symbols,
                KlineInterval.OneMinute,
                update => Close(category, update, trades, closed),
                CancellationToken.None),
            _ => throw new NotSupportedException($"Candles of {category} instruments of Bybit are not streamed.")
        };
    }

    private UpdateSubscription Subscribed(BybitCategory category, WebSocketResult<UpdateSubscription> result)
    {
        if (!result.Success)
        {
            throw new TransientExchangeException($"Bybit did not take a subscription to its {category} stream: {result.Error?.Message}");
        }

        result.Data.ConnectionLost += () => logger.LogWarning("A connection of the {Category} stream of Bybit is lost.", category);
        result.Data.ConnectionRestored += lost => logger.LogInformation("A connection of the {Category} stream of Bybit is back after {Lost}.", category, lost);

        return result.Data;
    }

    // The side of a trade is that of its taker: a Buy is a trade the buyer started. A trade that completes the last closed minute of
    // its symbol has the candle of that minute told again.
    private void Count(BybitCategory category, BybitTrade[] update, BybitMinuteTrades trades, Action<string, Candle> closed)
    {
        foreach (BybitTrade trade in update)
        {
            try
            {
                if (trades.Add(trade.Symbol, Utc(trade.Timestamp), trade.Quantity, trade.Price, trade.Side == OrderSide.Buy) is { } completed)
                {
                    closed(trade.Symbol, completed);
                }
            }
            catch (Exception exception)
            {
                // A listener that failed: the candle stays as it was told at first, the stream goes on.
                logger.LogError(exception, "A candle of {Category} {Symbol} completed by a late trade is dropped.", category, trade.Symbol);
            }
        }
    }

    // Bybit pushes a candle while its minute goes on and once more, confirmed, when it closes; only that one is a closed candle.
    private void Close(BybitCategory category, DataEvent<BybitKlineUpdate[]> update, BybitMinuteTrades trades, Action<string, Candle> closed)
    {
        foreach (BybitKlineUpdate kline in update.Data.Where(kline => kline.Confirm))
        {
            try
            {
                string symbol = update.Symbol ?? throw new InvalidOperationException("A candle of the stream names no symbol.");
                var candle = new Candle(
                    Utc(kline.StartTime),
                    kline.OpenPrice,
                    kline.HighPrice,
                    kline.LowPrice,
                    kline.ClosePrice,
                    kline.Volume,
                    kline.Turnover);

                closed(symbol, trades.Close(symbol, candle));
            }
            catch (Exception exception)
            {
                // A candle Bybit told wrong or a listener that failed: its minute is left to the loading of candles, the stream goes on.
                logger.LogError(exception, "A closed candle of {Category} {Symbol} of the stream of Bybit is dropped.", category, update.Symbol);
            }
        }
    }

    // Runs on the timer once a second: the forming candles of the symbols that traded since the last time.
    private void TellForming(BybitCategory category, BybitMinuteTrades trades, Action<string, Candle> forming)
    {
        try
        {
            foreach ((string symbol, Candle candle) in trades.TakeForming())
            {
                forming(symbol, candle);
            }
        }
        catch (Exception exception)
        {
            // A listener that failed: the candles of this second are dropped, the next second tells them anew.
            logger.LogError(exception, "The forming candles of the {Category} stream of Bybit are dropped.", category);
        }
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    private sealed class Subscription(ITimer forming) : IAsyncDisposable
    {
        private readonly List<UpdateSubscription> _subscriptions = [];

        public void Add(UpdateSubscription subscription)
        {
            _subscriptions.Add(subscription);
        }

        public async ValueTask DisposeAsync()
        {
            await forming.DisposeAsync();
            await Task.WhenAll(_subscriptions.Select(subscription => subscription.CloseAsync()));
        }
    }
}