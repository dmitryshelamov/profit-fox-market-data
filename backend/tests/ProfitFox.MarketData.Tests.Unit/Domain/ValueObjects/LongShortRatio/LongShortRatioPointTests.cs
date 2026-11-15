using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.LongShortRatio;

[TestFixture]
public class LongShortRatioPointTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    [Test]
    public void A_contract_every_account_is_long_in_has_a_short_share_of_zero()
    {
        Assert.That(() => new LongShortRatioPoint(Btc, Minute, longShare: 1m, shortShare: 0m), Throws.Nothing);
    }

    [Test]
    public void A_share_below_zero_is_rejected_the_long_one_and_the_short_one()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new LongShortRatioPoint(Btc, Minute, longShare: -0.0001m, shortShare: 1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LongShortRatioPoint(Btc, Minute, longShare: 1m, shortShare: -0.0001m), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }
}