using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments;

[TestFixture]
public class InstrumentRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _repository = _services.GetRequiredService<IInstrumentRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private static readonly Guid LinearId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid SpotId = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private static readonly Guid ShareId = Guid.Parse("0193a1f0-0000-7000-8000-000000000003");

    private const string ShareUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private ServiceProvider _services = null!;
    private IInstrumentRepository _repository = null!;

    [Test]
    public async Task Instruments_of_both_exchanges_come_back_as_they_were_saved()
    {
        Instrument linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear(), Then, LinearId);
        Instrument share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate("SBER", tickSize: 0.01m, ShareUid), Then, ShareId);
        share.Delist(Now);

        await _repository.SaveAsync([share, linear], CancellationToken.None);
        IReadOnlyList<Instrument> stored = await _repository.GetAllAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stored.Select(instrument => instrument.Id), Is.EqualTo(new[] { LinearId, ShareId }));
            Assert.That(stored.Select(instrument => instrument.Listing), Is.EqualTo(new[] { linear.Listing, share.Listing }));
            Assert.That(stored.Select(instrument => instrument.DelistedAt), Is.EqualTo(new DateTimeOffset?[] { null, Now }));
        });
    }

    [Test]
    public async Task Saved_instruments_come_back_as_they_were()
    {
        // Linear fills every optional value of a listing, spot leaves them empty.
        Instrument linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear(), Then, LinearId);
        Instrument spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot("LUNA"), Then, SpotId);
        spot.Delist(Now);

        await _repository.SaveAsync([linear, spot], CancellationToken.None);
        IReadOnlyList<Instrument> stored = await _repository.GetAllAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stored.Select(instrument => instrument.Id), Is.EqualTo(new[] { LinearId, SpotId }));
            Assert.That(stored.Select(instrument => instrument.Listing), Is.EqualTo(new[] { linear.Listing, spot.Listing }));
            Assert.That(stored.Select(instrument => instrument.FirstSeenAt), Is.EqualTo(new[] { Then, Then }));
            Assert.That(stored.Select(instrument => instrument.DelistedAt), Is.EqualTo(new DateTimeOffset?[] { null, Now }));
        });
    }

    [Test]
    public async Task Saving_a_stored_instrument_replaces_it()
    {
        Instrument instrument = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.1m));
        instrument.Delist(Now);
        await _repository.SaveAsync([instrument], CancellationToken.None);
        instrument.Refresh(BybitListedInstrumentGenerator.GenerateLinear(tickSize: 0.5m));

        await _repository.SaveAsync([instrument], CancellationToken.None);
        Instrument stored = (await _repository.GetAllAsync(CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(((BybitListedInstrument)stored.Listing).Rules.TickSize, Is.EqualTo(0.5m));
            Assert.That(stored.DelistedAt, Is.Null);
        });
    }

    [Test]
    public async Task Saving_a_stored_share_replaces_its_listing()
    {
        Instrument share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate("YNDX", uid: ShareUid));
        await _repository.SaveAsync([share], CancellationToken.None);
        TInvestListedShare renamed = TInvestListedShareGenerator.Generate("YDEX", tickSize: 0.5m, ShareUid);
        share.Refresh(renamed);

        await _repository.SaveAsync([share], CancellationToken.None);
        Instrument stored = (await _repository.GetAllAsync(CancellationToken.None)).Single();

        Assert.That(stored.Listing, Is.EqualTo(renamed));
    }
}