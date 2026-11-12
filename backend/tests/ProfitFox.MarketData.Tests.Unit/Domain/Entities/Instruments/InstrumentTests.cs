using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Instruments;

[TestFixture]
public class InstrumentTests
{
    private static readonly Guid Id = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void Create_makes_an_instrument_first_seen_now()
    {
        var listing = new BybitListedInstrument(
            BybitCategory.Linear,
            "BTCUSDT",
            "BTC",
            "USDT",
            InstrumentStatus.Trading,
            new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m),
            BybitContractType.LinearPerpetual);

        var instrument = Instrument.Create(Id, listing, Now);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Id, Is.EqualTo(Id));
            Assert.That(instrument.Listing, Is.SameAs(listing));
            Assert.That(instrument.FirstSeenAt, Is.EqualTo(Now));
        });
    }
}