using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class ClosedCandleFeedTests
{
    [SetUp]
    public void SetUp()
    {
        _feed = new ClosedCandleFeed();
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid InstrumentId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private ClosedCandleFeed _feed = null!;

    private static ClosedCandle CandleAt(int minute)
    {
        return new ClosedCandle(Exchange.Bybit, InstrumentId, CandleGenerator.Generate(Minute.AddMinutes(minute)));
    }

    // The next candles that wait for the subscriber; fails when fewer come in five seconds.
    private static async Task<IReadOnlyList<ClosedCandle>> TakeAsync(IClosedCandleSubscription subscription, int count)
    {
        var taken = new List<ClosedCandle>();
        using var patience = new CancellationTokenSource(Patience);

        await foreach (ClosedCandle candle in subscription.ReadAllAsync(patience.Token))
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
        IClosedCandleSubscription subscription = _feed.Subscribe();
        _feed.Publish([CandleAt(minute: 0)]);

        subscription.Dispose();
        _feed.Publish([CandleAt(minute: 1)]);

        var left = new List<ClosedCandle>();

        await foreach (ClosedCandle candle in subscription.ReadAllAsync(CancellationToken.None))
        {
            left.Add(candle);
        }

        Assert.That(left, Is.EqualTo(new[] { CandleAt(minute: 0) }));
    }

    [Test]
    public async Task A_subscriber_gets_what_is_published_after_it_subscribed_in_order()
    {
        _feed.Publish([CandleAt(minute: 0)]);
        using IClosedCandleSubscription subscription = _feed.Subscribe();

        _feed.Publish([CandleAt(minute: 1), CandleAt(minute: 2)]);
        _feed.Publish([CandleAt(minute: 3)]);

        Assert.That(await TakeAsync(subscription, count: 3), Is.EqualTo(new[] { CandleAt(minute: 1), CandleAt(minute: 2), CandleAt(minute: 3) }));
    }

    [Test]
    public async Task A_subscriber_that_falls_behind_is_cut_off_after_what_fitted_and_the_others_go_on()
    {
        using IClosedCandleSubscription slow = _feed.Subscribe();
        using IClosedCandleSubscription other = _feed.Subscribe();
        ClosedCandle[] flood = [.. Enumerable.Range(start: 0, ClosedCandleFeed.Capacity).Select(CandleAt)];

        _feed.Publish(flood);
        IReadOnlyList<ClosedCandle> ofOther = await TakeAsync(other, ClosedCandleFeed.Capacity);
        _feed.Publish([CandleAt(ClosedCandleFeed.Capacity)]);

        var ofSlow = new List<ClosedCandle>();
        SubscriberFellBehindException? cutOff = Assert.ThrowsAsync<SubscriberFellBehindException>(async () =>
        {
            await foreach (ClosedCandle candle in slow.ReadAllAsync(CancellationToken.None))
            {
                ofSlow.Add(candle);
            }
        });

        Assert.Multiple(async () =>
        {
            Assert.That(cutOff, Is.Not.Null);
            Assert.That(ofSlow, Has.Count.EqualTo(ClosedCandleFeed.Capacity));
            Assert.That(ofOther, Has.Count.EqualTo(ClosedCandleFeed.Capacity));
            Assert.That(await TakeAsync(other, count: 1), Is.EqualTo(new[] { CandleAt(ClosedCandleFeed.Capacity) }));
        });
    }

    [Test]
    public async Task Every_subscriber_gets_every_candle()
    {
        using IClosedCandleSubscription first = _feed.Subscribe();
        using IClosedCandleSubscription second = _feed.Subscribe();

        _feed.Publish([CandleAt(minute: 0)]);

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeAsync(first, count: 1), Is.EqualTo(new[] { CandleAt(minute: 0) }));
            Assert.That(await TakeAsync(second, count: 1), Is.EqualTo(new[] { CandleAt(minute: 0) }));
        });
    }
}