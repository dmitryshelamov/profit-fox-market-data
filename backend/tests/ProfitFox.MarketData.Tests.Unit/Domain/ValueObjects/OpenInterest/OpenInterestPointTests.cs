using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.OpenInterest;

[TestFixture]
public class OpenInterestPointTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    [Test]
    public void A_contract_nobody_holds_has_a_point_of_zero()
    {
        Assert.That(() => new OpenInterestPoint(Btc, Minute, value: 0m), Throws.Nothing);
    }

    [Test]
    public void A_negative_open_interest_is_rejected()
    {
        Assert.That(() => new OpenInterestPoint(Btc, Minute, value: -0.001m), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Points_of_the_same_instrument_time_and_value_are_equal()
    {
        var first = new OpenInterestPoint(Btc, Minute, value: 52345.123m);
        var second = new OpenInterestPoint(Btc, Minute, value: 52345.123m);

        Assert.That(first, Is.EqualTo(second));
    }
}