using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

[TestFixture]
public class GetLongShortRatioQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _series = _scope.ServiceProvider.GetRequiredService<ILongShortRatioSeriesRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILongShortRatioSeriesRepository _series = null!;

    private Task<LongShortRatioPointsInternal> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetLongShortRatioQuery(instrumentId, new PointWindow(from, to)), CancellationToken.None);
    }

    // A series of the instrument that covers [from, until) with the points.
    private Task LoadAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset until, params (DateTimeOffset Time, decimal Long)[] points)
    {
        return _series.SaveAsync(
            LongShortRatioSeries.Create(
                instrument.Id,
                from,
                until,
                [.. points.Select(point => new LongShortRatioPoint(instrument.Id, point.Time, point.Long, 1m - point.Long))]),
            CancellationToken.None);
    }

    // A point a step of the instrument from the hour on, five minutes apart, the long share of each the number of its step in ten thousandths.
    private Task LoadStepsAsync(int count)
    {
        return LoadAsync(Btc, Hour, Hour.AddMinutes(5 * count), [.. Enumerable.Range(start: 0, count).Select(step => (Hour.AddMinutes(5 * step), step / 10000m))]);
    }

    [Test]
    public async Task A_window_of_1000_points_gives_them_all_and_nothing_to_continue_from()
    {
        await LoadStepsAsync(count: 1000);

        LongShortRatioPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddDays(4));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Has.Count.EqualTo(1000));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task A_window_of_more_than_1000_points_is_cut_and_tells_where_the_rest_starts()
    {
        await LoadStepsAsync(count: 1001);

        LongShortRatioPointsInternal first = await ReadAsync(Btc.Id, Hour, Hour.AddDays(4));
        LongShortRatioPointsInternal rest = await ReadAsync(Btc.Id, first.ContinueFrom!.Value, Hour.AddDays(4));

        Assert.Multiple(() =>
        {
            Assert.That(first.Points, Has.Count.EqualTo(1000));
            Assert.That(first.Points[0], Is.EqualTo(new LongShortRatioPointInternal(Hour, Long: 0m, Short: 1m)));
            Assert.That(first.Points[^1], Is.EqualTo(new LongShortRatioPointInternal(Hour.AddMinutes(5 * 999), Long: 0.0999m, Short: 0.9001m)));
            Assert.That(first.ContinueFrom, Is.EqualTo(Hour.AddMinutes(5 * 1000)));
            Assert.That(rest.Points, Is.EqualTo(new[] { new LongShortRatioPointInternal(Hour.AddMinutes(5 * 1000), Long: 0.1m, Short: 0.9m) }));
            Assert.That(rest.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_of_the_catalog_with_nothing_loaded_tells_no_points_and_no_coverage()
    {
        LongShortRatioPointsInternal points = await ReadAsync(Eth.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Is.Empty);
            Assert.That(points.CoveredFrom, Is.Null);
            Assert.That(points.CoveredUntil, Is.Null);
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_not_found()
    {
        Assert.That(() => ReadAsync(Guid.NewGuid(), Hour, Hour.AddHours(1)), Throws.InstanceOf<InstrumentNotFoundException>());
    }

    [Test]
    public async Task An_instrument_with_no_points_in_the_window_has_an_empty_answer()
    {
        await LoadAsync(Btc, Hour.AddHours(2), Hour.AddHours(3), (Hour.AddHours(2), 0.7m));

        LongShortRatioPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Is.Empty);
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task The_points_of_the_window_come_oldest_first_as_they_are_stored_with_the_coverage_of_the_whole_series()
    {
        // A point before the window, one at its end and one of another instrument are not of it; the steps between the points have none
        // at the exchange, and nothing stands in for them.
        await LoadAsync(
            Btc,
            Hour.AddDays(-30),
            Hour.AddHours(1),
            (Hour.AddMinutes(25), 0.7302m),
            (Hour.AddMinutes(-5), 0.1m),
            (Hour, 0.726m),
            (Hour.AddMinutes(5), 0.5m),
            (Hour.AddMinutes(50), 0.4m));
        await LoadAsync(Eth, Hour, Hour.AddHours(1), (Hour.AddMinutes(5), 0.9m));

        LongShortRatioPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddMinutes(50));

        Assert.Multiple(() =>
        {
            Assert.That(
                points.Points,
                Is.EqualTo(
                    new[]
                    {
                        new LongShortRatioPointInternal(Hour, Long: 0.726m, Short: 0.274m),
                        new LongShortRatioPointInternal(Hour.AddMinutes(5), Long: 0.5m, Short: 0.5m),
                        new LongShortRatioPointInternal(Hour.AddMinutes(25), Long: 0.7302m, Short: 0.2698m)
                    }));
            Assert.That(points.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(points.CoveredUntil, Is.EqualTo(Hour.AddHours(1)));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }
}