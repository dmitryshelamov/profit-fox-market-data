using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Instruments;

[TestFixture]
public class BybitListedInstrumentTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m);

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 20, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [TestCase(BybitContractType.LinearPerpetual)]
    [TestCase(BybitContractType.LinearFutures)]
    public void A_linear_contract_keeps_its_contract_type_and_launch_time(BybitContractType contractType)
    {
        BybitListedInstrument instrument = Create(BybitCategory.Linear, contractType);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.ContractType, Is.EqualTo(contractType));
            Assert.That(instrument.LaunchTime, Is.EqualTo(LaunchTime));
            Assert.That(instrument.Exchange, Is.EqualTo(Exchange.Bybit));
        });
    }

    [TestCase("", "BTC", "USDT")]
    [TestCase("BTCUSDT", " ", "USDT")]
    [TestCase("BTCUSDT", "BTC", "")]
    public void A_blank_symbol_or_asset_is_rejected(string symbol, string baseAsset, string quoteAsset)
    {
        Assert.Throws<ArgumentException>(() =>
            new BybitListedInstrument(BybitCategory.Spot, symbol, baseAsset, quoteAsset, BybitInstrumentStatus.Trading, Rules));
    }

    private static BybitListedInstrument Create(BybitCategory category, BybitContractType? contractType)
    {
        return new BybitListedInstrument(category, "BTCUSDT", "BTC", "USDT", BybitInstrumentStatus.Trading, Rules, contractType, LaunchTime);
    }

    [Test]
    public void A_linear_contract_without_a_contract_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Create(BybitCategory.Linear, contractType: null));
    }

    [Test]
    public void A_spot_pair_with_a_contract_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Create(BybitCategory.Spot, BybitContractType.LinearPerpetual));
    }

    [Test]
    public void Spot_has_no_contract_type()
    {
        var instrument = new BybitListedInstrument(BybitCategory.Spot, "BTCUSDT", "BTC", "USDT", BybitInstrumentStatus.Trading, Rules);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Category, Is.EqualTo(BybitCategory.Spot));
            Assert.That(instrument.ContractType, Is.Null);
            Assert.That(instrument.LaunchTime, Is.Null);
            Assert.That(instrument.Rules, Is.EqualTo(Rules));
        });
    }
}