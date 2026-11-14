using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Instruments;

[TestFixture]
public class InstrumentRulesTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public void Non_positive_values_are_rejected(decimal value)
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => new InstrumentRules(value, quantityStep: 1m, minQuantity: 1m),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new InstrumentRules(tickSize: 1m, value, minQuantity: 1m),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new InstrumentRules(tickSize: 1m, quantityStep: 1m, value),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new InstrumentRules(tickSize: 1m, quantityStep: 1m, minQuantity: 1m, value),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Min_amount_is_optional()
    {
        var rules = new InstrumentRules(tickSize: 0.01m, quantityStep: 0.1m, minQuantity: 0.1m);

        Assert.That(rules.MinAmount, Is.Null);
    }

    [Test]
    public void Rules_with_same_values_are_equal()
    {
        var first = new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m);
        var second = new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m);

        Assert.That(first, Is.EqualTo(second));
    }
}