using ProfitFox.MarketData.Application.Queries;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries;

[TestFixture]
public class HistoryWindowTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = Today.AddHours(6);

    [Test]
    public void A_window_up_to_the_start_of_today_holds_its_ends()
    {
        var window = new HistoryWindow(Today.AddDays(-3), Today, Now);

        Assert.Multiple(() =>
        {
            Assert.That(window.From, Is.EqualTo(Today.AddDays(-3)));
            Assert.That(window.To, Is.EqualTo(Today));
        });
    }

    [Test]
    public void A_window_into_today_is_rejected()
    {
        Assert.That(() => new HistoryWindow(Today.AddDays(-1), Today.AddHours(1), Now), Throws.ArgumentException);
    }

    [Test]
    public void Today_is_the_utc_day_of_now_whatever_its_offset()
    {
        // 02:00 on the 17th in Moscow is 23:00 on the 16th in UTC: the 16th is still going on.
        var moscow = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 2, minute: 0, second: 0, TimeSpan.FromHours(3));

        Assert.Multiple(() =>
        {
            Assert.That(() => new HistoryWindow(Today.AddDays(-2), Today.AddDays(-1), moscow), Throws.Nothing);
            Assert.That(() => new HistoryWindow(Today.AddDays(-2), Today, moscow), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_window_that_does_not_end_after_it_starts_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new HistoryWindow(Today.AddDays(-1), Today.AddDays(-1), Now), Throws.ArgumentException);
            Assert.That(() => new HistoryWindow(Today.AddDays(-1), Today.AddDays(-2), Now), Throws.ArgumentException);
        });
    }
}