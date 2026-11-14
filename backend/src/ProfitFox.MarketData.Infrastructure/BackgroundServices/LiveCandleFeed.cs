using System.Threading.Channels;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Tells the live candles to the subscribers of the app, in memory only. Each subscriber has a place of its own where candles wait
///     for it, at most <see cref="Capacity" />; publishing never waits for a subscriber. One that reads too slowly first stops getting
///     forming candles, once more than <see cref="FormingLimit" /> wait for it, and is cut off when a closed candle no longer fits,
///     instead of holding back the saving of candles or the other subscribers.
/// </summary>
internal sealed class LiveCandleFeed : ILiveCandleFeed
{
    // About ten minutes of the closed candles of the whole market.
    internal const int Capacity = 15_000;

    // A few seconds of the forming candles of the whole market: a subscriber that far behind gets no more of them until it caught up.
    internal const int FormingLimit = 5_000;

    private readonly Lock _lock = new Lock();

    private readonly List<Subscription> _subscriptions = [];

    public void Publish(IReadOnlyCollection<LiveCandle> candles)
    {
        Subscription[] subscriptions;

        lock (_lock)
        {
            subscriptions = [.. _subscriptions];
        }

        foreach (Subscription subscription in subscriptions)
        {
            subscription.Take(candles);
        }
    }

    public ILiveCandleSubscription Subscribe(bool forming)
    {
        var subscription = new Subscription(this, forming);

        lock (_lock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private sealed class Subscription(LiveCandleFeed feed, bool forming) : ILiveCandleSubscription
    {
        private readonly Channel<LiveCandle> _waiting = Channel.CreateBounded<LiveCandle>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false });

        public void Take(IReadOnlyCollection<LiveCandle> candles)
        {
            foreach (LiveCandle candle in candles)
            {
                if (!candle.Closed)
                {
                    if (forming && _waiting.Reader.Count < FormingLimit)
                    {
                        _waiting.Writer.TryWrite(candle);
                    }

                    continue;
                }

                if (!_waiting.Writer.TryWrite(candle))
                {
                    // No room: the subscriber reads what fitted and is told it fell behind. A subscription already ended takes nothing.
                    _waiting.Writer.TryComplete(new SubscriberFellBehindException());
                    feed.Remove(this);

                    return;
                }
            }
        }

        public IAsyncEnumerable<LiveCandle> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _waiting.Reader.ReadAllAsync(cancellationToken);
        }

        public void Dispose()
        {
            feed.Remove(this);
            _waiting.Writer.TryComplete();
        }
    }
}