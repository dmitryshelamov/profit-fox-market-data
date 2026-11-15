using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class LiveCandleFeedTests
{
    [SetUp]
    public void SetUp()
    {
        _feed = new LiveCandleFeed();
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid InstrumentId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Exchange[] Bybit = [Exchange.Bybit];

    private LiveCandleFeed _feed = null!;

    private static LiveCandle CandleAt(int minute)
    {
        return new LiveCandle(Exchange.Bybit, InstrumentId, CandleGenerator.Generate(Minute.AddMinutes(minute)), Closed: true);
    }

    private static LiveCandle FormingAt(int minute, decimal close = 5m)
    {
        return new LiveCandle(Exchange.Bybit, InstrumentId, CandleGenerator.Generate(Minute.AddMinutes(minute), close), Closed: false);
    }

    // The next candles that wait for the subscriber; fails when fewer come in five seconds.
    private static async Task<IReadOnlyList<LiveCandle>> TakeAsync(ILiveCandleSubscription subscription, int count)
    {
        var taken = new List<LiveCandle>();
        using var patience = new CancellationTokenSource(Patience);

        await foreach (LiveCandle candle in subscription.ReadAllAsync(patience.Token))
        {
            taken.Add(candle);

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
        ILiveCandleSubscription subscription = _feed.Subscribe(Bybit, forming: false);
        _feed.Publish([CandleAt(minute: 0)]);

        subscription.Dispose();
        _feed.Publish([CandleAt(minute: 1)]);

        var left = new List<LiveCandle>();

        await foreach (LiveCandle candle in subscription.ReadAllAsync(CancellationToken.None))
        {
            left.Add(candle);
        }

        Assert.That(left, Is.EqualTo(new[] { CandleAt(minute: 0) }));
    }

    [Test]
    public async Task A_forming_candle_goes_only_to_the_subscribers_that_asked_for_them()
    {
        using ILiveCandleSubscription withForming = _feed.Subscribe(Bybit, forming: true);
        using ILiveCandleSubscription closedOnly = _feed.Subscribe(Bybit, forming: false);

        _feed.Publish([FormingAt(minute: 0), CandleAt(minute: 0)]);

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeAsync(withForming, count: 2), Is.EqualTo(new[] { FormingAt(minute: 0), CandleAt(minute: 0) }));
            Assert.That(await TakeAsync(closedOnly, count: 1), Is.EqualTo(new[] { CandleAt(minute: 0) }));
        });
    }

    [Test]
    public async Task A_subscriber_far_behind_gets_no_forming_candles_until_it_caught_up_and_every_closed_one()
    {
        using ILiveCandleSubscription subscription = _feed.Subscribe(Bybit, forming: true);
        LiveCandle[] backlog = [.. Enumerable.Range(start: 0, LiveCandleFeed.FormingLimit).Select(CandleAt)];
        _feed.Publish(backlog);

        _feed.Publish([FormingAt(minute: 0, close: 6m), CandleAt(LiveCandleFeed.FormingLimit)]);
        IReadOnlyList<LiveCandle> waiting = await TakeAsync(subscription, LiveCandleFeed.FormingLimit + 1);
        _feed.Publish([FormingAt(minute: 0, close: 7m)]);
        IReadOnlyList<LiveCandle> afterCatchingUp = await TakeAsync(subscription, count: 1);

        Assert.Multiple(() =>
        {
            Assert.That(waiting.Select(candle => candle.Closed), Is.All.True);
            Assert.That(waiting[^1], Is.EqualTo(CandleAt(LiveCandleFeed.FormingLimit)));
            Assert.That(afterCatchingUp, Is.EqualTo(new[] { FormingAt(minute: 0, close: 7m) }));
        });
    }

    [Test]
    public async Task A_subscriber_gets_what_is_published_after_it_subscribed_in_order()
    {
        _feed.Publish([CandleAt(minute: 0)]);
        using ILiveCandleSubscription subscription = _feed.Subscribe(Bybit, forming: false);

        _feed.Publish([CandleAt(minute: 1), CandleAt(minute: 2)]);
        _feed.Publish([CandleAt(minute: 3)]);

        Assert.That(await TakeAsync(subscription, count: 3), Is.EqualTo(new[] { CandleAt(minute: 1), CandleAt(minute: 2), CandleAt(minute: 3) }));
    }

    [Test]
    public async Task A_subscriber_that_falls_behind_is_cut_off_after_what_fitted_and_the_others_go_on()
    {
        using ILiveCandleSubscription slow = _feed.Subscribe(Bybit, forming: false);
        using ILiveCandleSubscription other = _feed.Subscribe(Bybit, forming: false);
        LiveCandle[] flood = [.. Enumerable.Range(start: 0, LiveCandleFeed.Capacity).Select(CandleAt)];

        _feed.Publish(flood);
        IReadOnlyList<LiveCandle> ofOther = await TakeAsync(other, LiveCandleFeed.Capacity);
        _feed.Publish([CandleAt(LiveCandleFeed.Capacity)]);

        var ofSlow = new List<LiveCandle>();
        SubscriberFellBehindException? cutOff = Assert.ThrowsAsync<SubscriberFellBehindException>(async () =>
        {
            await foreach (LiveCandle candle in slow.ReadAllAsync(CancellationToken.None))
            {
                ofSlow.Add(candle);
            }
        });

        Assert.Multiple(async () =>
        {
            Assert.That(cutOff, Is.Not.Null);
            Assert.That(ofSlow, Has.Count.EqualTo(LiveCandleFeed.Capacity));
            Assert.That(ofOther, Has.Count.EqualTo(LiveCandleFeed.Capacity));
            Assert.That(await TakeAsync(other, count: 1), Is.EqualTo(new[] { CandleAt(LiveCandleFeed.Capacity) }));
        });
    }

    [Test]
    public async Task Candles_of_exchanges_not_asked_for_never_wait_for_a_subscriber()
    {
        using ILiveCandleSubscription shares = _feed.Subscribe([Exchange.TInvest], forming: false);
        LiveCandle ofShare = CandleAt(minute: 0) with { Exchange = Exchange.TInvest };

        // More candles of Bybit than a subscription holds: one that asked for them would be cut off.
        _feed.Publish([.. Enumerable.Range(start: 0, LiveCandleFeed.Capacity + 1).Select(CandleAt)]);
        _feed.Publish([ofShare]);

        Assert.That(await TakeAsync(shares, count: 1), Is.EqualTo(new[] { ofShare }));
    }

    [Test]
    public async Task Every_subscriber_gets_every_candle()
    {
        using ILiveCandleSubscription first = _feed.Subscribe(Bybit, forming: false);
        using ILiveCandleSubscription second = _feed.Subscribe(Bybit, forming: false);

        _feed.Publish([CandleAt(minute: 0)]);

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeAsync(first, count: 1), Is.EqualTo(new[] { CandleAt(minute: 0) }));
            Assert.That(await TakeAsync(second, count: 1), Is.EqualTo(new[] { CandleAt(minute: 0) }));
        });
    }
}