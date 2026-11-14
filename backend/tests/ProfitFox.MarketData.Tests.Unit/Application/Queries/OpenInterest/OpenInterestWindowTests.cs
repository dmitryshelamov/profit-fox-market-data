using ProfitFox.MarketData.Application.Queries.OpenInterest;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries.OpenInterest;

[TestFixture]
public class OpenInterestWindowTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void A_window_may_start_and_end_at_any_moment_and_be_of_any_length()
    {
        Assert.That(() => new OpenInterestWindow(Noon.AddSeconds(17), Noon.AddDays(400)), Throws.Nothing);
    }

    [Test]
    public void A_window_that_does_not_end_after_it_starts_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new OpenInterestWindow(Noon, Noon), Throws.ArgumentException);
            Assert.That(() => new OpenInterestWindow(Noon.AddMinutes(1), Noon), Throws.ArgumentException);
        });
    }
}