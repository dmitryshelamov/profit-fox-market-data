using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Liquidations;

[TestFixture]
public class LiquidationSumTests
{
    [Test]
    public void A_liquidation_adds_its_size_what_it_was_worth_and_one_to_the_count()
    {
        LiquidationSum sum = new LiquidationSum(volume: 3m, turnover: 30m, count: 2).Add(size: 2m, price: 5m);

        Assert.That(sum, Is.EqualTo(new LiquidationSum(volume: 5m, turnover: 40m, count: 3)));
    }

    [Test]
    public void A_minute_nothing_was_liquidated_in_sums_to_nothing()
    {
        Assert.That(LiquidationSum.None, Is.EqualTo(new LiquidationSum(volume: 0m, turnover: 0m, count: 0)));
    }
}