using System.Threading.Channels;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Tells market data of one kind to the subscribers of the app, in memory only. Each subscriber has a place of its own where the
///     items wait for it, at most <see cref="Capacity" />; publishing never waits for a subscriber. One that reads too slowly is cut
///     off when an item no longer fits, instead of holding back the saving or the other subscribers.
/// </summary>
internal sealed class LiveFeed<T> : ILiveFeed<T>
{
    // About ten snapshots of the whole market: fifty minutes of the points a collection saves, far more of rarer ones.
    internal const int Capacity = 10_000;

    private readonly Lock _lock = new Lock();

    private readonly List<Subscription> _subscriptions = [];

    public void Publish(IReadOnlyCollection<T> items)
    {
        Subscription[] subscriptions;

        lock (_lock)
        {
            subscriptions = [.. _subscriptions];
        }

        foreach (Subscription subscription in subscriptions)
        {
            subscription.Take(items);
        }
    }

    public ILiveSubscription<T> Subscribe()
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

    private sealed class Subscription(LiveFeed<T> feed) : ILiveSubscription<T>
    {
        private readonly Channel<T> _waiting = Channel.CreateBounded<T>(new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false });

        private readonly Lock _taking = new Lock();

        public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _waiting.Reader.ReadAllAsync(cancellationToken);
        }

        public void Dispose()
        {
            feed.Remove(this);
            _waiting.Writer.TryComplete();
        }

        public void Take(IReadOnlyCollection<T> items)
        {
            // One publisher at a time: between an item that did not fit and the end of the subscription the subscriber may free a
            // place, and an item of another publisher written there would come after a gap.
            lock (_taking)
            {
                foreach (T item in items)
                {
                    if (!_waiting.Writer.TryWrite(item))
                    {
                        // No room: the subscriber reads what fitted and is told it fell behind. A subscription already ended takes nothing.
                        _waiting.Writer.TryComplete(new SubscriberFellBehindException());
                        feed.Remove(this);

                        return;
                    }
                }
            }
        }
    }
}