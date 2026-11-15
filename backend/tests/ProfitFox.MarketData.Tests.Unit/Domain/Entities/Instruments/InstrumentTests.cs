using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Instruments;

[TestFixture]
public class InstrumentTests
{
    private static readonly Guid Id = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void Create_makes_a_listed_instrument_first_seen_now()
    {
        BybitListedInstrument listing = BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.1m);

        var instrument = Instrument.Create(Id, listing, Now);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Id, Is.EqualTo(Id));
            Assert.That(instrument.Listing, Is.SameAs(listing));
            Assert.That(instrument.FirstSeenAt, Is.EqualTo(Now));
            Assert.That(instrument.DelistedAt, Is.Null);
        });
    }

    [Test]
    public void Delist_keeps_the_time_it_went()
    {
        var instrument = Instrument.Create(Id, BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.1m), Now);

        instrument.Delist(Now.AddDays(1));
        instrument.Delist(Now.AddDays(2));

        Assert.That(instrument.DelistedAt, Is.EqualTo(Now.AddDays(1)));
    }

    [Test]
    public void Refresh_takes_the_new_listing_and_lists_a_delisted_instrument_again()
    {
        var instrument = Instrument.Create(Id, BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.1m), Now);
        instrument.Delist(Now.AddDays(1));
        BybitListedInstrument listing = BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.5m);

        instrument.Refresh(listing);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Listing, Is.SameAs(listing));
            Assert.That(instrument.DelistedAt, Is.Null);
        });
    }
}