using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Liquidations;

[TestFixture]
public class LiquidationMinuteTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly LiquidationSum Longs = new LiquidationSum(volume: 3m, turnover: 30m, count: 2);

    private static readonly LiquidationSum Shorts = new LiquidationSum(volume: 1m, turnover: 9m, count: 1);

    [Test]
    public void A_liquidated_long_adds_to_the_longs_and_leaves_the_shorts()
    {
        LiquidationMinute minute = new LiquidationMinute(Btc, Minute, Longs, Shorts).Add(ofLong: true, size: 2m, price: 5m);

        Assert.That(minute, Is.EqualTo(new LiquidationMinute(Btc, Minute, new LiquidationSum(volume: 5m, turnover: 40m, count: 3), Shorts)));
    }

    [Test]
    public void A_liquidated_short_adds_to_the_shorts_and_leaves_the_longs()
    {
        LiquidationMinute minute = new LiquidationMinute(Btc, Minute, Longs, Shorts).Add(ofLong: false, size: 2m, price: 5m);

        Assert.That(minute, Is.EqualTo(new LiquidationMinute(Btc, Minute, Longs, new LiquidationSum(volume: 3m, turnover: 19m, count: 2))));
    }

    [Test]
    public void A_time_that_is_not_the_start_of_a_minute_is_rejected()
    {
        Assert.That(() => new LiquidationMinute(Btc, Minute.AddSeconds(1), Longs, Shorts), Throws.ArgumentException);
    }
}