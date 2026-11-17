using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     The coverage and the points of the history of open interest: the two reads of one call.
/// </summary>
[TestFixture]
public class GetOpenInterestHistoryQueryHandlersTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth, Spot], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Nine = Today.AddDays(-3).AddHours(9);

    // The ids put BTC before ETH among the points of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly Instrument Spot = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateSpot(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000003"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private Task StoreSeriesAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset until, params OpenInterestPoint[] points)
    {
        return _scope.ServiceProvider.GetRequiredService<IOpenInterestSeriesRepository>()
            .SaveAsync(OpenInterestSeries.Create(instrument.Id, from, until, points), CancellationToken.None);
    }

    private async Task<IReadOnlyList<InstrumentOpenInterestInternal>> ReadAsync(DateTimeOffset from, DateTimeOffset to, params Guid[] instrumentIds)
    {
        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();
        IReadOnlyList<InstrumentCoverageInternal> coverage =
            await sender.Send(new GetOpenInterestHistoryCoverageQuery(instrumentIds, [], new HistoryWindow(from, to, Today.AddHours(6))));
        var read = new List<InstrumentOpenInterestInternal>();

        await foreach (InstrumentOpenInterestInternal point in sender.CreateStream(new GetOpenInterestHistoryQuery(coverage)))
        {
            read.Add(point);
        }

        return read;
    }

    [Test]
    public async Task The_points_of_the_window_inside_the_coverage_come_exactly_as_stored()
    {
        await StoreSeriesAsync(
            Btc,
            Nine,
            Nine.AddHours(3),
            new OpenInterestPoint(Btc.Id, Nine.AddMinutes(55), value: 1m),
            new OpenInterestPoint(Btc.Id, Nine.AddHours(1), value: 52345.123m),
            new OpenInterestPoint(Btc.Id, Nine.AddHours(1).AddMinutes(5), value: 52350m),
            new OpenInterestPoint(Btc.Id, Nine.AddHours(2), value: 9m));

        IReadOnlyList<InstrumentOpenInterestInternal> points = await ReadAsync(Nine.AddHours(1), Nine.AddHours(2), Btc.Id);

        Assert.That(
            points,
            Is.EqualTo(
                new[]
                {
                    new InstrumentOpenInterestInternal(Btc.Id, Nine.AddHours(1), 52345.123m),
                    new InstrumentOpenInterestInternal(Btc.Id, Nine.AddHours(1).AddMinutes(5), 52350m)
                }));
    }

    [Test]
    public async Task A_point_collected_outside_the_coverage_does_not_come()
    {
        await StoreSeriesAsync(Btc, Nine, Nine.AddHours(1), new OpenInterestPoint(Btc.Id, Nine, value: 1m));
        await _scope.ServiceProvider.GetRequiredService<IOpenInterestRepository>()
            .SaveAsync([new OpenInterestPoint(Btc.Id, Nine.AddHours(2), value: 2m)], CancellationToken.None);

        IReadOnlyList<InstrumentOpenInterestInternal> points = await ReadAsync(Nine, Today, Btc.Id);

        Assert.That(points.Select(point => point.Time), Is.EqualTo(new[] { Nine }));
    }

    [Test]
    public async Task Points_come_by_time_and_then_by_instrument_id()
    {
        await StoreSeriesAsync(Eth, Nine, Nine.AddMinutes(10), new OpenInterestPoint(Eth.Id, Nine, value: 2m), new OpenInterestPoint(Eth.Id, Nine.AddMinutes(5), value: 2m));
        await StoreSeriesAsync(Btc, Nine, Nine.AddMinutes(10), new OpenInterestPoint(Btc.Id, Nine, value: 1m), new OpenInterestPoint(Btc.Id, Nine.AddMinutes(5), value: 1m));

        IReadOnlyList<InstrumentOpenInterestInternal> points = await ReadAsync(Nine, Today);

        Assert.That(
            points.Select(point => (point.Time, point.InstrumentId)),
            Is.EqualTo(new[] { (Nine, Btc.Id), (Nine, Eth.Id), (Nine.AddMinutes(5), Btc.Id), (Nine.AddMinutes(5), Eth.Id) }));
    }

    [Test]
    public async Task A_spot_pair_has_no_coverage_and_no_points()
    {
        IReadOnlyList<InstrumentCoverageInternal> coverage = await _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetOpenInterestHistoryCoverageQuery([Spot.Id], [], new HistoryWindow(Nine, Today, Today.AddHours(6))));

        Assert.Multiple(async () =>
        {
            Assert.That(coverage.Single().InstrumentId, Is.EqualTo(Spot.Id));
            Assert.That(coverage.Single().Ranges, Is.Empty);
            Assert.That(await ReadAsync(Nine, Today, Spot.Id), Is.Empty);
        });
    }

    [Test]
    public async Task The_coverage_of_a_window_wider_than_the_series_is_the_series()
    {
        await StoreSeriesAsync(Btc, Nine, Nine.AddHours(1));

        IReadOnlyList<InstrumentCoverageInternal> coverage = await _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetOpenInterestHistoryCoverageQuery([Btc.Id], [], new HistoryWindow(Nine.AddDays(-400), Today, Today.AddHours(6))));

        Assert.That(coverage.Single().Ranges, Is.EqualTo(new[] { new TimeRangeInternal(Nine, Nine.AddHours(1)) }));
    }
}