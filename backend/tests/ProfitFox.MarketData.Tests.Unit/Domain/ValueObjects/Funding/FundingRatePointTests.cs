using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Funding;

[TestFixture]
public class FundingRatePointTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    // The shorts pay the longs, or nobody pays.
    [TestCase("-0.00012")]
    [TestCase("0")]
    public void A_rate_that_is_not_above_zero_is_a_point(string rate)
    {
        Assert.That(() => new FundingRatePoint(Btc, Minute, decimal.Parse(rate)), Throws.Nothing);
    }

    [Test]
    public void Points_of_the_same_instrument_time_and_rate_are_equal()
    {
        var first = new FundingRatePoint(Btc, Minute, rate: 0.00000331m);
        var second = new FundingRatePoint(Btc, Minute, rate: 0.00000331m);

        Assert.That(first, Is.EqualTo(second));
    }
}