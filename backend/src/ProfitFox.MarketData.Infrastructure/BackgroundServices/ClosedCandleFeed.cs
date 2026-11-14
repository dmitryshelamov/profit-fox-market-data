using System.Threading.Channels;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Tells the closed candles to the subscribers of the app, in memory only. Each subscriber has a place of its own where candles
///     wait for it, at most <see cref="Capacity" />; publishing never waits for a subscriber, so one that reads too slowly is cut off
///     instead of holding back the saving of candles or the other subscribers.
/// </summary>
internal sealed class ClosedCandleFeed : IClosedCandleFeed
{
    // About ten minutes of the closed candles of the whole market.
    internal const int Capacity = 15_000;

    private readonly Lock _lock = new Lock();

    private readonly List<Subscription> _subscriptions = [];

    public void Publish(IReadOnlyCollection<ClosedCandle> candles)
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

    public IClosedCandleSubscription Subscribe()
    {
        var subscription = new Subscription(this);

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

    private sealed class Subscription(ClosedCandleFeed feed) : IClosedCandleSubscription
    {
        private readonly Channel<ClosedCandle> _waiting = Channel.CreateBounded<ClosedCandle>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false });

        public void Take(IReadOnlyCollection<ClosedCandle> candles)
        {
            foreach (ClosedCandle candle in candles)
            {
                if (!_waiting.Writer.TryWrite(candle))
                {
                    // No room: the subscriber reads what fitted and is told it fell behind. A subscription already ended takes nothing.
                    _waiting.Writer.TryComplete(new SubscriberFellBehindException());
                    feed.Remove(this);

                    return;
                }
            }
        }

        public IAsyncEnumerable<ClosedCandle> ReadAllAsync(CancellationToken cancellationToken)
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