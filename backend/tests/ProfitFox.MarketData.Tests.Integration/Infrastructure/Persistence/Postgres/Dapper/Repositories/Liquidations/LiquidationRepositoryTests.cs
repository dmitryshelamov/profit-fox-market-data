using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations;

[TestFixture]
public class LiquidationRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<ILiquidationRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly LiquidationSum Longs = new LiquidationSum(volume: 0.75m, turnover: 76125.5m, count: 3);

    private static readonly LiquidationSum Shorts = new LiquidationSum(volume: 0.001m, turnover: 101.337m, count: 1);

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILiquidationRepository _repository = null!;

    // The stored minutes of every instrument with their marks, by instrument and time.
    private static async Task<IReadOnlyList<(LiquidationMinute Minute, bool Valid)>> StoredAsync()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync(
                """
                SELECT instrument_id, time, long_volume, long_turnover, long_count, short_volume, short_turnover, short_count, is_valid
                FROM liquidations
                ORDER BY instrument_id, time
                """))
            .Select(row => (
                new LiquidationMinute(
                    (Guid)row.instrument_id,
                    new DateTimeOffset((DateTime)row.time, TimeSpan.Zero),
                    new LiquidationSum((decimal)row.long_volume, (decimal)row.long_turnover, (int)row.long_count),
                    new LiquidationSum((decimal)row.short_volume, (decimal)row.short_turnover, (int)row.short_count)),
                (bool)row.is_valid))
        ];
    }

    private static IEnumerable<(LiquidationMinute Minute, bool Valid)> Given(IReadOnlyList<StoredLiquidationMinute> stored)
    {
        return stored.Select(minute => (minute.Minute, minute.IsValid));
    }

    private static LiquidationMinute Nothing(Instrument instrument, DateTimeOffset time)
    {
        return new LiquidationMinute(instrument.Id, time, LiquidationSum.None, LiquidationSum.None);
    }

    [Test]
    public async Task A_minute_marked_not_valid_keeps_its_sums()
    {
        var heard = new LiquidationMinute(Btc.Id, Minute, Longs, Shorts);
        await _repository.AddAsync([heard], CancellationToken.None);

        IReadOnlyList<StoredLiquidationMinute> marked = await _repository.InvalidateAsync([Btc.Id], Minute, Minute, CancellationToken.None);

        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(new[] { (heard, false) }));
            Assert.That(Given(marked), Is.EqualTo(new[] { (heard, false) }));
        });
    }

    [Test]
    public async Task A_minute_not_valid_already_is_left_as_it_is_and_is_not_given_back()
    {
        await _repository.InvalidateAsync([Btc.Id], Minute, Minute.AddMinutes(1), CancellationToken.None);

        IReadOnlyList<StoredLiquidationMinute> marked = await _repository.InvalidateAsync([Btc.Id], Minute.AddMinutes(1), Minute.AddMinutes(2), CancellationToken.None);

        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Given(marked), Is.EqualTo(new[] { (Nothing(Btc, Minute.AddMinutes(2)), false) }));
            Assert.That(rows, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task A_minute_not_stored_yet_is_stored_as_a_valid_one_exactly_as_it_was_summed()
    {
        LiquidationMinute[] minutes = [new LiquidationMinute(Btc.Id, Minute, Longs, LiquidationSum.None), new LiquidationMinute(Eth.Id, Minute, Longs, Shorts)];

        IReadOnlyList<StoredLiquidationMinute> stored = await _repository.AddAsync(minutes, CancellationToken.None);

        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(minutes.Select(minute => (minute, true))));
            Assert.That(Given(stored), Is.EqualTo(minutes.Select(minute => (minute, true))));
        });
    }

    [Test]
    public async Task A_stored_minute_takes_the_sums_on_top_of_its_own_and_the_other_minutes_stay()
    {
        LiquidationMinute other = LiquidationMinuteGenerator.Generate(Btc.Id, Minute.AddMinutes(1));
        await _repository.AddAsync([new LiquidationMinute(Btc.Id, Minute, Longs, Shorts), other], CancellationToken.None);

        var more = new LiquidationMinute(
            Btc.Id,
            Minute,
            new LiquidationSum(volume: 0.25m, turnover: 25000m, count: 1),
            new LiquidationSum(volume: 2m, turnover: 200m, count: 4));
        IReadOnlyList<StoredLiquidationMinute> stored = await _repository.AddAsync([more], CancellationToken.None);

        var summed = new LiquidationMinute(
            Btc.Id,
            Minute,
            new LiquidationSum(volume: 1m, turnover: 101125.5m, count: 4),
            new LiquidationSum(volume: 2.001m, turnover: 301.337m, count: 5));
        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(new[] { (summed, true), (other, true) }));

            // Only the minute the call added to, with all that is stored of it now.
            Assert.That(Given(stored), Is.EqualTo(new[] { (summed, true) }));
        });
    }

    [Test]
    public async Task Every_minute_of_a_span_is_marked_for_every_instrument_both_ends_included_and_one_not_stored_gets_sums_of_nothing()
    {
        IReadOnlyList<StoredLiquidationMinute> marked = await _repository.InvalidateAsync([Btc.Id, Eth.Id], Minute, Minute.AddMinutes(2), CancellationToken.None);

        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                rows,
                Is.EqualTo(
                    new[]
                    {
                        (Nothing(Btc, Minute), false),
                        (Nothing(Btc, Minute.AddMinutes(1)), false),
                        (Nothing(Btc, Minute.AddMinutes(2)), false),
                        (Nothing(Eth, Minute), false),
                        (Nothing(Eth, Minute.AddMinutes(1)), false),
                        (Nothing(Eth, Minute.AddMinutes(2)), false)
                    }));

            // Given back by time first, as a follower takes them.
            Assert.That(
                Given(marked),
                Is.EqualTo(
                    new[]
                    {
                        (Nothing(Btc, Minute), false),
                        (Nothing(Eth, Minute), false),
                        (Nothing(Btc, Minute.AddMinutes(1)), false),
                        (Nothing(Eth, Minute.AddMinutes(1)), false),
                        (Nothing(Btc, Minute.AddMinutes(2)), false),
                        (Nothing(Eth, Minute.AddMinutes(2)), false)
                    }));
        });
    }

    [Test]
    public async Task Nothing_stored_has_no_latest_minute()
    {
        Assert.That(await _repository.GetLastTimeAsync(CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Sums_added_to_a_minute_that_is_not_valid_leave_it_not_valid()
    {
        await _repository.InvalidateAsync([Btc.Id], Minute, Minute, CancellationToken.None);

        IReadOnlyList<StoredLiquidationMinute> stored = await _repository.AddAsync([new LiquidationMinute(Btc.Id, Minute, Longs, Shorts)], CancellationToken.None);

        IReadOnlyList<(LiquidationMinute Minute, bool Valid)> rows = await StoredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(new[] { (new LiquidationMinute(Btc.Id, Minute, Longs, Shorts), false) }));
            Assert.That(Given(stored), Is.EqualTo(new[] { (new LiquidationMinute(Btc.Id, Minute, Longs, Shorts), false) }));
        });
    }

    [Test]
    public async Task The_latest_stored_minute_is_told_whatever_its_instrument_and_its_mark()
    {
        await _repository.AddAsync([LiquidationMinuteGenerator.Generate(Btc.Id, Minute)], CancellationToken.None);
        await _repository.InvalidateAsync([Eth.Id], Minute.AddMinutes(3), Minute.AddMinutes(3), CancellationToken.None);

        Assert.That(await _repository.GetLastTimeAsync(CancellationToken.None), Is.EqualTo(Minute.AddMinutes(3)));
    }
}