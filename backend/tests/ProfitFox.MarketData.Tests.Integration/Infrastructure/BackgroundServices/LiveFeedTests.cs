using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class LiveFeedTests
{
    [SetUp]
    public void SetUp()
    {
        _feed = new LiveFeed<int>();
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private LiveFeed<int> _feed = null!;

    // The next items that wait for the subscriber; fails when fewer come in five seconds.
    private static async Task<IReadOnlyList<int>> TakeAsync(ILiveSubscription<int> subscription, int count)
    {
        var taken = new List<int>();
        using var patience = new CancellationTokenSource(Patience);

        await foreach (int item in subscription.ReadAllAsync(patience.Token))
        {
            taken.Add(item);

            if (taken.Count == count)
            {
                break;
            }
        }

        return taken;
    }

    [Test]
    public async Task A_disposed_subscription_ends_and_takes_nothing_more()
    {
        ILiveSubscription<int> subscription = _feed.Subscribe();
        _feed.Publish([1]);

        subscription.Dispose();
        _feed.Publish([2]);

        // What waited before the end is still read; the read then ends by itself.
        Assert.That(await TakeAsync(subscription, count: 2), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task A_subscriber_gets_what_is_published_while_it_is_subscribed_in_the_order_it_was_published()
    {
        _feed.Publish([1]);
        using ILiveSubscription<int> subscription = _feed.Subscribe();

        _feed.Publish([2, 3]);
        _feed.Publish([4]);

        Assert.That(await TakeAsync(subscription, count: 3), Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public async Task A_subscriber_that_reads_too_slowly_is_cut_off_after_what_fitted_and_the_others_go_on()
    {
        using ILiveSubscription<int> slow = _feed.Subscribe();
        _feed.Publish([.. Enumerable.Range(start: 0, LiveFeed<int>.Capacity)]);
        using ILiveSubscription<int> late = _feed.Subscribe();

        // One more than the slow subscriber has room for; the late one has all the room.
        _feed.Publish([-1]);
        _feed.Publish([-2]);

        var read = new List<int>();
        Assert.ThrowsAsync<SubscriberFellBehindException>(async () =>
        {
            await foreach (int item in slow.ReadAllAsync(CancellationToken.None))
            {
                read.Add(item);
            }
        });
        Assert.Multiple(async () =>
        {
            Assert.That(read, Has.Count.EqualTo(LiveFeed<int>.Capacity));
            Assert.That(await TakeAsync(late, count: 2), Is.EqualTo(new[] { -1, -2 }));
        });
    }

    [Test]
    public async Task Every_subscriber_gets_every_item()
    {
        using ILiveSubscription<int> first = _feed.Subscribe();
        using ILiveSubscription<int> second = _feed.Subscribe();

        _feed.Publish([1, 2]);

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeAsync(first, count: 2), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(await TakeAsync(second, count: 2), Is.EqualTo(new[] { 1, 2 }));
        });
    }
}