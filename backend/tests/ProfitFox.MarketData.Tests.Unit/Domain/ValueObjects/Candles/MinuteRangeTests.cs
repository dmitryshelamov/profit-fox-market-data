using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Candles;

[TestFixture]
public class MinuteRangeTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void An_empty_range_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new MinuteRange(From, From), Throws.ArgumentException);
            Assert.That(() => new MinuteRange(From, From.AddMinutes(-1)), Throws.ArgumentException);
        });
    }

    [Test]
    public void An_end_inside_a_minute_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new MinuteRange(From.AddSeconds(30), From.AddMinutes(5)), Throws.ArgumentException);
            Assert.That(() => new MinuteRange(From, From.AddMinutes(5).AddMilliseconds(1)), Throws.ArgumentException);
        });
    }
}