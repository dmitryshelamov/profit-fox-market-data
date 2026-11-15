using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest;

[TestFixture]
public class OpenInterestSeriesRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IOpenInterestSeriesRepository>();
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
    private IOpenInterestSeriesRepository _repository = null!;

    // The stored points of an instrument, oldest first.
    private static async Task<IReadOnlyList<OpenInterestPoint>> StoredAsync(Instrument instrument)
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync<(DateTime Time, decimal Value)>(
                "SELECT time, value FROM open_interest WHERE instrument_id = @Id ORDER BY time",
                new { instrument.Id }))
            .Select(row => new OpenInterestPoint(instrument.Id, new DateTimeOffset(row.Time, TimeSpan.Zero), row.Value))
        ];
    }

    // What the collection writes: a point as the ticker told it.
    private Task CollectAsync(params OpenInterestPoint[] points)
    {
        return _scope.ServiceProvider.GetRequiredService<IOpenInterestRepository>().SaveAsync(points, CancellationToken.None);
    }

    [Test]
    public async Task A_point_the_collection_kept_is_not_replaced_by_the_one_of_the_history()
    {
        // The ticker told the value exactly; the history tells the same one rounded.
        await CollectAsync(new OpenInterestPoint(Btc.Id, Noon, value: 38.16m));
        var series = OpenInterestSeries.Create(
            Btc.Id,
            Noon.AddMinutes(-5),
            Noon.AddMinutes(5),
            [new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 38.2m), new OpenInterestPoint(Btc.Id, Noon, value: 38.15999999m)]);

        await _repository.SaveAsync(series, CancellationToken.None);

        Assert.That(
            await StoredAsync(Btc),
            Is.EqualTo(new[] { new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 38.2m), new OpenInterestPoint(Btc.Id, Noon, value: 38.16m) }));
    }

    [Test]
    public async Task A_range_without_points_is_saved_as_coverage_alone()
    {
        await _repository.SaveAsync(OpenInterestSeries.Create(Btc.Id, Noon.AddDays(-1), Noon, []), CancellationToken.None);

        OpenInterestSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

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
        OpenInterestPoint[] points = [new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-10), value: 70772.729m), new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 70711.87699999m)];

        await _repository.SaveAsync(OpenInterestSeries.Create(Btc.Id, Noon.AddMinutes(-10), Noon, points), CancellationToken.None);
        OpenInterestSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

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
        await _repository.SaveAsync(
            OpenInterestSeries.Create(Btc.Id, Noon.AddMinutes(-5), Noon, [new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 1m)]),
            CancellationToken.None);
        OpenInterestSeries series = (await _repository.GetAsync(Btc.Id, CancellationToken.None))!;

        series.Cover(Noon, Noon.AddMinutes(5), [new OpenInterestPoint(Btc.Id, Noon, value: 2m)]);
        await _repository.SaveAsync(series, CancellationToken.None);
        OpenInterestSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.CoveredFrom, Is.EqualTo(Noon.AddMinutes(-5)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Noon.AddMinutes(5)));
            Assert.That(
                await StoredAsync(Btc),
                Is.EqualTo(new[] { new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 1m), new OpenInterestPoint(Btc.Id, Noon, value: 2m) }));
        });
    }

    [Test]
    public async Task An_instrument_with_nothing_loaded_has_no_series()
    {
        // Points of the collection alone are not a series: nothing tells how far they are whole.
        await CollectAsync(new OpenInterestPoint(Eth.Id, Noon, value: 5m));

        Assert.That(await _repository.GetAsync(Eth.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Points_that_cannot_be_stored_leave_the_coverage_unsaved_too()
    {
        // The catalog has no such instrument, so the rows of the series break their foreign keys.
        var unknown = Guid.NewGuid();
        var series = OpenInterestSeries.Create(unknown, Noon.AddMinutes(-5), Noon, [new OpenInterestPoint(unknown, Noon.AddMinutes(-5), value: 1m)]);

        Assert.ThrowsAsync<PostgresException>(() => _repository.SaveAsync(series, CancellationToken.None));

        Assert.That(await _repository.GetAsync(unknown, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task The_points_of_a_range_are_counted_whoever_stored_them_the_end_of_the_range_left_out()
    {
        await CollectAsync(
            new OpenInterestPoint(Btc.Id, Noon.AddMinutes(-5), value: 1m),
            new OpenInterestPoint(Btc.Id, Noon, value: 2m),
            new OpenInterestPoint(Btc.Id, Noon.AddMinutes(10), value: 3m),
            new OpenInterestPoint(Eth.Id, Noon, value: 9m));

        Assert.Multiple(async () =>
        {
            Assert.That(await _repository.CountPointsAsync(Btc.Id, Noon.AddMinutes(-5), Noon.AddMinutes(10), CancellationToken.None), Is.EqualTo(2));
            Assert.That(await _repository.CountPointsAsync(Btc.Id, Noon.AddMinutes(-5), Noon.AddMinutes(15), CancellationToken.None), Is.EqualTo(3));
            Assert.That(await _repository.CountPointsAsync(Btc.Id, Noon.AddDays(1), Noon.AddDays(2), CancellationToken.None), Is.Zero);
        });
    }
}