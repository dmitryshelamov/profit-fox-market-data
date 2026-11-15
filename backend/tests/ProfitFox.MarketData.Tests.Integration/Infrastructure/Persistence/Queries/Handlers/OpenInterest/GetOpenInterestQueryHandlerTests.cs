using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

[TestFixture]
public class GetOpenInterestQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
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

    private Task<OpenInterestPointsInternal> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetOpenInterestQuery(instrumentId, new PointWindow(from, to)), CancellationToken.None);
    }

    private Task StoreAsync(params OpenInterestPoint[] points)
    {
        return _scope.ServiceProvider.GetRequiredService<IOpenInterestRepository>().SaveAsync(points, CancellationToken.None);
    }

    // A point a step of the instrument from the hour on, five minutes apart, each with the number of its step.
    private Task StoreStepsAsync(int count)
    {
        return StoreAsync([.. Enumerable.Range(start: 0, count).Select(step => new OpenInterestPoint(Btc.Id, Hour.AddMinutes(5 * step), step))]);
    }

    [Test]
    public async Task A_window_of_1000_points_gives_them_all_and_nothing_to_continue_from()
    {
        await StoreStepsAsync(count: 1000);

        OpenInterestPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddDays(4));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Has.Count.EqualTo(1000));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task A_window_of_more_than_1000_points_is_cut_and_tells_where_the_rest_starts()
    {
        await StoreStepsAsync(count: 1001);

        OpenInterestPointsInternal first = await ReadAsync(Btc.Id, Hour, Hour.AddDays(4));
        OpenInterestPointsInternal rest = await ReadAsync(Btc.Id, first.ContinueFrom!.Value, Hour.AddDays(4));

        Assert.Multiple(() =>
        {
            Assert.That(first.Points, Has.Count.EqualTo(1000));
            Assert.That(first.Points[0], Is.EqualTo(new OpenInterestPointInternal(Hour, OpenInterest: 0m)));
            Assert.That(first.Points[^1], Is.EqualTo(new OpenInterestPointInternal(Hour.AddMinutes(5 * 999), OpenInterest: 999m)));
            Assert.That(first.ContinueFrom, Is.EqualTo(Hour.AddMinutes(5 * 1000)));
            Assert.That(rest.Points, Is.EqualTo(new[] { new OpenInterestPointInternal(Hour.AddMinutes(5 * 1000), OpenInterest: 1000m) }));
            Assert.That(rest.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_of_the_catalog_with_no_history_loaded_tells_no_coverage()
    {
        // The collection kept a point, but nothing tells how far the points of the instrument are whole.
        await StoreAsync(new OpenInterestPoint(Btc.Id, Hour, value: 5m));

        OpenInterestPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Has.Count.EqualTo(1));
            Assert.That(points.CoveredFrom, Is.Null);
            Assert.That(points.CoveredUntil, Is.Null);
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
        await StoreAsync(new OpenInterestPoint(Btc.Id, Hour.AddHours(2), value: 5m));

        OpenInterestPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Is.Empty);
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task The_points_of_the_window_come_oldest_first_as_they_are_stored()
    {
        // A point before the window, one at its end and one of another instrument are not of it; the steps between them were not
        // collected, and nothing stands in for them.
        await StoreAsync(
            new OpenInterestPoint(Btc.Id, Hour.AddMinutes(25), value: 0.00000331m),
            new OpenInterestPoint(Btc.Id, Hour.AddMinutes(-5), value: 1m),
            new OpenInterestPoint(Btc.Id, Hour, value: 2m),
            new OpenInterestPoint(Btc.Id, Hour.AddMinutes(5), value: 3m),
            new OpenInterestPoint(Btc.Id, Hour.AddMinutes(50), value: 4m),
            new OpenInterestPoint(Eth.Id, Hour.AddMinutes(5), value: 5m));

        OpenInterestPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddMinutes(50));

        Assert.Multiple(() =>
        {
            Assert.That(
                points.Points,
                Is.EqualTo(
                    new[]
                    {
                        new OpenInterestPointInternal(Hour, OpenInterest: 2m),
                        new OpenInterestPointInternal(Hour.AddMinutes(5), OpenInterest: 3m),
                        new OpenInterestPointInternal(Hour.AddMinutes(25), OpenInterest: 0.00000331m)
                    }));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task The_points_of_the_window_come_with_the_coverage_of_the_whole_series()
    {
        await _scope.ServiceProvider.GetRequiredService<IOpenInterestSeriesRepository>()
            .SaveAsync(
                OpenInterestSeries.Create(
                    Btc.Id,
                    Hour.AddDays(-30),
                    Hour.AddMinutes(10),
                    [new OpenInterestPoint(Btc.Id, Hour, value: 2m), new OpenInterestPoint(Btc.Id, Hour.AddMinutes(5), value: 3m)]),
                CancellationToken.None);

        // A window with no points in it, inside the coverage: the contract was listed, and had nothing there.
        OpenInterestPointsInternal quiet = await ReadAsync(Btc.Id, Hour.AddDays(-2), Hour.AddDays(-1));
        OpenInterestPointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(quiet.Points, Is.Empty);
            Assert.That(quiet.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(points.Points, Is.EqualTo(new[] { new OpenInterestPointInternal(Hour, OpenInterest: 2m) }));

            // The coverage is of the series, not of the window.
            Assert.That(points.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(points.CoveredUntil, Is.EqualTo(Hour.AddMinutes(10)));
        });
    }
}