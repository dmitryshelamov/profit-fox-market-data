using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Instruments;

[TestFixture]
public class SyncBybitInstrumentsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = Substitute.For<IBybitInstrumentsClient>();
        _repository = Substitute.For<IInstrumentRepository>();
        _repository.SaveAsync(Arg.Do<IReadOnlyCollection<Instrument>>(instruments => _saved = instruments), Arg.Any<CancellationToken>());

        _handler = new SyncBybitInstrumentsCommandHandler(_bybit, _repository);
    }

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private IBybitInstrumentsClient _bybit = null!;
    private IInstrumentRepository _repository = null!;
    private IReadOnlyCollection<Instrument> _saved = [];
    private SyncBybitInstrumentsCommandHandler _handler = null!;

    private Task SyncLinear()
    {
        return _handler.Handle(new SyncBybitInstrumentsCommand(BybitCategory.Linear, Now), CancellationToken.None);
    }

    private void Stored(params Instrument[] instruments)
    {
        // As the repository does: only the instruments of the exchange asked for.
        _repository.GetByExchangeAsync(Arg.Any<Exchange>(), Arg.Any<CancellationToken>())
            .Returns(call => instruments.Where(instrument => instrument.Listing.Exchange == call.Arg<Exchange>()).ToList());
    }

    private void Listed(BybitCategory category, params BybitListedInstrument[] listings)
    {
        _bybit.ListAsync(category, Arg.Any<CancellationToken>()).Returns(listings);
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

    [Test]
    public async Task Shares_of_tinvest_stay_as_they_are()
    {
        TInvestListedShare share = TInvestListedShareGenerator.Generate();
        Instrument tinvest = InstrumentGenerator.Generate(share);
        BybitListedInstrument linear = BybitListedInstrumentGenerator.GenerateLinear();
        Stored(tinvest);
        Listed(BybitCategory.Linear, linear);

        await SyncLinear();

        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().Listing, Is.SameAs(linear));
            Assert.That(tinvest.Listing, Is.SameAs(share));
            Assert.That(tinvest.DelistedAt, Is.Null);
        });
    }
}