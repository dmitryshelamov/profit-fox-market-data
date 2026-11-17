using ProfitFox.MarketData.Application.Queries.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries.Candles;

[TestFixture]
public class CandleHistoryWindowTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = Today.AddHours(6);

    [TestCase(CandleInterval.OneHour, arg2: 30)]
    [TestCase(CandleInterval.FourHours, arg2: 60)]
    [TestCase(CandleInterval.OneDay, arg2: 240)]
    public void An_end_inside_a_candle_of_the_interval_is_rejected(CandleInterval interval, int minutesIntoCandle)
    {
        DateTimeOffset inside = Today.AddDays(-2).AddMinutes(minutesIntoCandle);

        Assert.Multiple(() =>
        {
            Assert.That(() => new CandleHistoryWindow(interval, inside, Today, Now), Throws.ArgumentException);
            Assert.That(() => new CandleHistoryWindow(interval, Today.AddDays(-3), inside, Now), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_window_into_today_is_rejected()
    {
        Assert.That(() => new CandleHistoryWindow(CandleInterval.OneHour, Today.AddDays(-1), Today.AddHours(1), Now), Throws.ArgumentException);
    }

    [Test]
    public void A_window_of_years_is_taken()
    {
        var window = new CandleHistoryWindow(CandleInterval.FiveMinutes, Today.AddDays(-1095), Today, Now);

        Assert.Multiple(() =>
        {
            Assert.That(window.Interval, Is.EqualTo(CandleInterval.FiveMinutes));
            Assert.That(window.Window.From, Is.EqualTo(Today.AddDays(-1095)));
            Assert.That(window.Window.To, Is.EqualTo(Today));
        });
    }

    [Test]
    public void A_window_that_does_not_end_after_it_starts_is_rejected()
    {
        Assert.That(() => new CandleHistoryWindow(CandleInterval.OneDay, Today.AddDays(-1), Today.AddDays(-1), Now), Throws.ArgumentException);
    }
}