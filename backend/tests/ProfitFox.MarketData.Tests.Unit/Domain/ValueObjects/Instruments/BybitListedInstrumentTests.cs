using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Instruments;

[TestFixture]
public class BybitListedInstrumentTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m);

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 20, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [TestCase(BybitCategory.Linear, BybitContractType.LinearPerpetual)]
    [TestCase(BybitCategory.Linear, BybitContractType.LinearFutures)]
    [TestCase(BybitCategory.Inverse, BybitContractType.InversePerpetual)]
    [TestCase(BybitCategory.Inverse, BybitContractType.InverseFutures)]
    public void Contract_keeps_a_contract_type_of_its_category(BybitCategory category, BybitContractType contractType)
    {
        BybitListedInstrument instrument = Create(category, contractType);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.ContractType, Is.EqualTo(contractType));
            Assert.That(instrument.LaunchTime, Is.EqualTo(LaunchTime));
        });
    }

    [TestCase(BybitCategory.Linear, BybitContractType.InversePerpetual)]
    [TestCase(BybitCategory.Inverse, BybitContractType.LinearFutures)]
    [TestCase(BybitCategory.Spot, BybitContractType.LinearPerpetual)]
    [TestCase(BybitCategory.Option, BybitContractType.LinearFutures)]
    public void Contract_type_of_another_category_is_rejected(BybitCategory category, BybitContractType contractType)
    {
        Assert.Throws<ArgumentException>(() => Create(category, contractType));
    }

    [TestCase(BybitCategory.Linear)]
    [TestCase(BybitCategory.Inverse)]
    public void Contract_without_contract_type_is_rejected(BybitCategory category)
    {
        Assert.Throws<ArgumentException>(() => Create(category, contractType: null));
    }

    [TestCase("", "BTC", "USDT")]
    [TestCase("BTCUSDT", " ", "USDT")]
    [TestCase("BTCUSDT", "BTC", "")]
    public void Blank_symbol_or_coin_is_rejected(string symbol, string baseCoin, string quoteCoin)
    {
        Assert.Throws<ArgumentException>(() =>
            new BybitListedInstrument(BybitCategory.Spot, symbol, baseCoin, quoteCoin, InstrumentStatus.Trading, Rules));
    }

    private static BybitListedInstrument Create(BybitCategory category, BybitContractType? contractType)
    {
        return new BybitListedInstrument(category, "BTCUSDT", "BTC", "USDT", InstrumentStatus.Trading, Rules, contractType, LaunchTime);
    }

    [Test]
    public void Spot_has_no_contract_type()
    {
        var instrument = new BybitListedInstrument(BybitCategory.Spot, "BTCUSDT", "BTC", "USDT", InstrumentStatus.Trading, Rules);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Category, Is.EqualTo(BybitCategory.Spot));
            Assert.That(instrument.ContractType, Is.Null);
            Assert.That(instrument.LaunchTime, Is.Null);
            Assert.That(instrument.Rules, Is.EqualTo(Rules));
        });
    }
}