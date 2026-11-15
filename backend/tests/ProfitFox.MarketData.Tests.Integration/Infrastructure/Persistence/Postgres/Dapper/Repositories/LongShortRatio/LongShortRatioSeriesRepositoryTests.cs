using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio;

[TestFixture]
public class LongShortRatioSeriesRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<ILongShortRatioSeriesRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILongShortRatioSeriesRepository _repository = null!;

    // The stored points of an instrument, oldest first.
    private static async Task<IReadOnlyList<LongShortRatioPoint>> StoredAsync(Instrument instrument)
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync<(DateTime Time, decimal Long, decimal Short)>(
                "SELECT time, long, short FROM long_short_ratio WHERE instrument_id = @Id ORDER BY time",
                new { instrument.Id }))
            .Select(row => new LongShortRatioPoint(instrument.Id, new DateTimeOffset(row.Time, TimeSpan.Zero), row.Long, row.Short))
        ];
    }

    private static LongShortRatioPoint Point(Instrument instrument, DateTimeOffset time, decimal longShare)
    {
        return new LongShortRatioPoint(instrument.Id, time, longShare, 1m - longShare);
    }

    [Test]
    public async Task A_point_storage_already_holds_stays_as_it_is_when_its_step_is_covered_again()
    {
        var first = LongShortRatioSeries.Create(Btc.Id, Noon.AddMinutes(-5), Noon, [Point(Btc, Noon.AddMinutes(-5), longShare: 0.7302m)]);
        await _repository.SaveAsync(first, CancellationToken.None);
        LongShortRatioSeries series = (await _repository.GetAsync(Btc.Id, CancellationToken.None))!;

        series.Cover(Noon.AddMinutes(-5), Noon.AddMinutes(5), [Point(Btc, Noon.AddMinutes(-5), longShare: 0.1m), Point(Btc, Noon, longShare: 0.726m)]);
        await _repository.SaveAsync(series, CancellationToken.None);

        Assert.That(await StoredAsync(Btc), Is.EqualTo(new[] { Point(Btc, Noon.AddMinutes(-5), longShare: 0.7302m), Point(Btc, Noon, longShare: 0.726m) }));
    }

    [Test]
    public async Task A_range_without_points_is_saved_as_coverage_alone()
    {
        await _repository.SaveAsync(LongShortRatioSeries.Create(Btc.Id, Noon.AddDays(-1), Noon, []), CancellationToken.None);

        LongShortRatioSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.CoveredFrom, Is.EqualTo(Noon.AddDays(-1)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(await StoredAsync(Btc), Is.Empty);
        });
    }

    [Test]
    public async Task A_series_is_read_as_it_was_saved_with_no_points_to_save_again()
    {
        LongShortRatioPoint[] points = [Point(Btc, Noon.AddMinutes(-10), longShare: 0.7302m), Point(Btc, Noon.AddMinutes(-5), longShare: 0.726m)];

        await _repository.SaveAsync(LongShortRatioSeries.Create(Btc.Id, Noon.AddMinutes(-10), Noon, points), CancellationToken.None);
        LongShortRatioSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.InstrumentId, Is.EqualTo(Btc.Id));
            Assert.That(read.CoveredFrom, Is.EqualTo(Noon.AddMinutes(-10)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(read.NewPoints, Is.Empty);
            Assert.That(await StoredAsync(Btc), Is.EqualTo(points));
            Assert.That(await StoredAsync(Eth), Is.Empty);
        });
    }

    [Test]
    public async Task A_series_saved_again_takes_the_wider_coverage_and_adds_its_new_points()
    {
        var first = LongShortRatioSeries.Create(Btc.Id, Noon.AddMinutes(-5), Noon, [Point(Btc, Noon.AddMinutes(-5), longShare: 0.7m)]);
        await _repository.SaveAsync(first, CancellationToken.None);
        LongShortRatioSeries series = (await _repository.GetAsync(Btc.Id, CancellationToken.None))!;

        series.Cover(Noon, Noon.AddMinutes(5), [Point(Btc, Noon, longShare: 0.6m)]);
        await _repository.SaveAsync(series, CancellationToken.None);
        LongShortRatioSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.CoveredFrom, Is.EqualTo(Noon.AddMinutes(-5)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Noon.AddMinutes(5)));
            Assert.That(await StoredAsync(Btc), Is.EqualTo(new[] { Point(Btc, Noon.AddMinutes(-5), longShare: 0.7m), Point(Btc, Noon, longShare: 0.6m) }));
        });
    }

    [Test]
    public async Task An_instrument_with_nothing_loaded_has_no_series()
    {
        await _repository.SaveAsync(LongShortRatioSeries.Create(Btc.Id, Noon.AddMinutes(-5), Noon, []), CancellationToken.None);

        Assert.That(await _repository.GetAsync(Eth.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Points_that_cannot_be_stored_leave_the_coverage_unsaved_too()
    {
        // The catalog has no such instrument, so the rows of the series break their foreign keys.
        var unknown = Guid.NewGuid();
        var series = LongShortRatioSeries.Create(
            unknown,
            Noon.AddMinutes(-5),
            Noon,
            [new LongShortRatioPoint(unknown, Noon.AddMinutes(-5), longShare: 0.7m, shortShare: 0.3m)]);

        Assert.ThrowsAsync<PostgresException>(() => _repository.SaveAsync(series, CancellationToken.None));

        Assert.That(await _repository.GetAsync(unknown, CancellationToken.None), Is.Null);
    }
}