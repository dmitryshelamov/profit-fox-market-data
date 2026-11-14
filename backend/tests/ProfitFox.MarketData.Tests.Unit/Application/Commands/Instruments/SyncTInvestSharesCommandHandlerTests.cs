using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncTInvestShares;
using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Instruments;

[TestFixture]
public class SyncTInvestSharesCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _shares = Substitute.For<ITInvestSharesClient>();
        _repository = Substitute.For<IInstrumentRepository>();
        _repository.SaveAsync(Arg.Do<IReadOnlyCollection<Instrument>>(instruments => _saved = instruments), Arg.Any<CancellationToken>());

        _handler = new SyncTInvestSharesCommandHandler(_shares, _repository);
    }

    private const string Uid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private ITInvestSharesClient _shares = null!;
    private IInstrumentRepository _repository = null!;
    private IReadOnlyCollection<Instrument> _saved = [];
    private SyncTInvestSharesCommandHandler _handler = null!;

    private Task Sync()
    {
        return _handler.Handle(new SyncTInvestSharesCommand(Now), CancellationToken.None);
    }

    private void Stored(params Instrument[] instruments)
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instruments);
    }

    private void Listed(params TInvestListedShare[] shares)
    {
        _shares.ListAsync(Arg.Any<CancellationToken>()).Returns(shares);
    }

    [Test]
    public async Task A_known_share_takes_its_new_listing_under_a_new_ticker()
    {
        Instrument known = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate("YNDX", uid: Uid), Then);
        known.Delist(Then.AddDays(1));
        TInvestListedShare listing = TInvestListedShareGenerator.Generate("YDEX", uid: Uid);
        Stored(known);
        Listed(listing);

        await Sync();

        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.EqualTo(new[] { known }));
            Assert.That(known.Listing, Is.SameAs(listing));
            Assert.That(known.FirstSeenAt, Is.EqualTo(Then));
            Assert.That(known.DelistedAt, Is.Null);
        });
    }

    [Test]
    public async Task A_share_gone_from_the_list_is_delisted_at_the_request()
    {
        Instrument gone = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate("GAZP"));
        Stored(gone);
        Listed();

        await Sync();

        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.EqualTo(new[] { gone }));
            Assert.That(gone.DelistedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task A_share_new_to_the_catalog_is_added_first_seen_at_the_request()
    {
        TInvestListedShare listing = TInvestListedShareGenerator.Generate();
        Stored();
        Listed(listing);

        await Sync();

        Instrument added = _saved.Single();
        Assert.Multiple(() =>
        {
            Assert.That(added.Listing, Is.SameAs(listing));
            Assert.That(added.FirstSeenAt, Is.EqualTo(Now));
            Assert.That(added.Id, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task Instruments_of_bybit_stay_as_they_are()
    {
        BybitListedInstrument linear = BybitListedInstrumentGenerator.GenerateLinear();
        Instrument bybit = InstrumentGenerator.Generate(linear);
        TInvestListedShare share = TInvestListedShareGenerator.Generate();
        Stored(bybit);
        Listed(share);

        await Sync();

        Assert.Multiple(() =>
        {
            Assert.That(_saved.Single().Listing, Is.SameAs(share));
            Assert.That(bybit.Listing, Is.SameAs(linear));
            Assert.That(bybit.DelistedAt, Is.Null);
        });
    }
}