using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries.Candles;

[TestFixture]
public class CandleWindowTests
{
    private static readonly DateTimeOffset Midnight = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void A_window_of_whole_candles_holds_its_interval_and_range()
    {
        var window = new CandleWindow(CandleInterval.FourHours, Midnight.AddHours(4), Midnight.AddHours(12));

        Assert.Multiple(() =>
        {
            Assert.That(window.Interval, Is.EqualTo(CandleInterval.FourHours));
            Assert.That(window.Range, Is.EqualTo(new MinuteRange(Midnight.AddHours(4), Midnight.AddHours(12))));
        });
    }

    [Test]
    public void A_window_of_more_than_1000_candles_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new CandleWindow(CandleInterval.OneMinute, Midnight, Midnight.AddMinutes(1000)), Throws.Nothing);
            Assert.That(() => new CandleWindow(CandleInterval.OneMinute, Midnight, Midnight.AddMinutes(1001)), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_window_that_does_not_end_after_it_starts_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new CandleWindow(CandleInterval.OneHour, Midnight, Midnight), Throws.ArgumentException);
            Assert.That(() => new CandleWindow(CandleInterval.OneHour, Midnight.AddHours(1), Midnight), Throws.ArgumentException);
        });
    }

    [TestCase(CandleInterval.OneHour, 30)]
    [TestCase(CandleInterval.FourHours, 60)]
    [TestCase(CandleInterval.OneDay, 240)]
    public void An_end_inside_a_candle_of_the_interval_is_rejected(CandleInterval interval, int minutesIntoCandle)
    {
        DateTimeOffset inside = Midnight.AddMinutes(minutesIntoCandle);

        Assert.Multiple(() =>
        {
            Assert.That(() => new CandleWindow(interval, inside, Midnight.AddDays(1)), Throws.ArgumentException);
            Assert.That(() => new CandleWindow(interval, Midnight.AddDays(-1), inside), Throws.ArgumentException);
        });
    }
}