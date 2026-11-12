using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Instruments.Sync;
using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Instruments;

[TestFixture]
public class SyncInstrumentsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _exchange = Substitute.For<IExchangeInstruments>();
        _repository = Substitute.For<IInstrumentRepository>();
        _repository.SaveAsync(Arg.Do<IReadOnlyCollection<Instrument>>(instruments => _saved = instruments), Arg.Any<CancellationToken>());

        _handler = new SyncInstrumentsCommandHandler(_exchange, _repository);
    }

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private IExchangeInstruments _exchange = null!;
    private IInstrumentRepository _repository = null!;
    private IReadOnlyCollection<Instrument> _saved = [];
    private SyncInstrumentsCommandHandler _handler = null!;

    private Task SyncLinear()
    {
        return _handler.Handle(new SyncInstrumentsCommand(BybitCategory.Linear, Now), CancellationToken.None);
    }

    private void Stored(params Instrument[] instruments)
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instruments);
    }

    private void Listed(BybitCategory category, params BybitListedInstrument[] listings)
    {
        _exchange.ListAsync(category, Arg.Any<CancellationToken>()).Returns(listings);
    }

    [Test]
    public async Task A_known_instrument_takes_its_new_listing()
    {
        Instrument known = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.1m), Then);
        known.Delist(Then.AddDays(1));
        BybitListedInstrument listing = BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.5m);
        Stored(known);
        Listed(BybitCategory.Linear, listing);

        await SyncLinear();

        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.EqualTo(new[] { known }));
            Assert.That(known.Listing, Is.SameAs(listing));
            Assert.That(known.FirstSeenAt, Is.EqualTo(Then));
            Assert.That(known.DelistedAt, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_gone_from_the_list_is_delisted_at_the_request()
    {
        Instrument gone = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));
        Stored(gone);
        Listed(BybitCategory.Linear);

        await SyncLinear();

        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.EqualTo(new[] { gone }));
            Assert.That(gone.DelistedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task An_instrument_new_to_the_catalog_is_added_first_seen_at_the_request()
    {
        BybitListedInstrument listing = BybitListedInstrumentGenerator.GenerateLinear();
        Stored();
        Listed(BybitCategory.Linear, listing);

        await SyncLinear();

        Instrument added = _saved.Single();
        Assert.Multiple(() =>
        {
            Assert.That(added.Listing, Is.SameAs(listing));
            Assert.That(added.FirstSeenAt, Is.EqualTo(Now));
            Assert.That(added.Id, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task Instruments_of_another_category_stay_as_they_are()
    {
        BybitListedInstrument spot = BybitListedInstrumentGenerator.GenerateSpot();
        Instrument other = InstrumentGenerator.Generate(spot);
        BybitListedInstrument linear = BybitListedInstrumentGenerator.GenerateLinear();
        Stored(other);
        Listed(BybitCategory.Linear, linear);

        await SyncLinear();

        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().Listing, Is.SameAs(linear));
            Assert.That(other.Listing, Is.SameAs(spot));
            Assert.That(other.DelistedAt, Is.Null);
        });
    }
}