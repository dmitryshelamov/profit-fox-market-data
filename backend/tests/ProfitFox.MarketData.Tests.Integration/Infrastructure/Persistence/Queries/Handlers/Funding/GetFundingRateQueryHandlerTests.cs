using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Funding;

[TestFixture]
public class GetFundingRateQueryHandlerTests
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

    private Task<FundingRatePointsInternal> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetFundingRateQuery(instrumentId, new FundingRateWindow(from, to)), CancellationToken.None);
    }

    private Task StoreAsync(params FundingRatePoint[] points)
    {
        return _scope.ServiceProvider.GetRequiredService<IFundingRateRepository>().SaveAsync(points, CancellationToken.None);
    }

    // A point a minute of the instrument from the hour on, each with the number of its minute.
    private Task StoreMinutesAsync(int count)
    {
        return StoreAsync([.. Enumerable.Range(start: 0, count).Select(minute => new FundingRatePoint(Btc.Id, Hour.AddMinutes(minute), rate: minute))]);
    }

    [Test]
    public async Task A_window_of_1000_points_gives_them_all_and_nothing_to_continue_from()
    {
        await StoreMinutesAsync(count: 1000);

        FundingRatePointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Has.Count.EqualTo(1000));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task A_window_of_more_than_1000_points_is_cut_and_tells_where_the_rest_starts()
    {
        await StoreMinutesAsync(count: 1001);

        FundingRatePointsInternal first = await ReadAsync(Btc.Id, Hour, Hour.AddDays(1));
        FundingRatePointsInternal rest = await ReadAsync(Btc.Id, first.ContinueFrom!.Value, Hour.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(first.Points, Has.Count.EqualTo(1000));
            Assert.That(first.Points[0], Is.EqualTo(new FundingRatePointInternal(Hour, 0m)));
            Assert.That(first.Points[^1], Is.EqualTo(new FundingRatePointInternal(Hour.AddMinutes(999), 999m)));
            Assert.That(first.ContinueFrom, Is.EqualTo(Hour.AddMinutes(1000)));
            Assert.That(rest.Points, Is.EqualTo(new[] { new FundingRatePointInternal(Hour.AddMinutes(1000), 1000m) }));
            Assert.That(rest.ContinueFrom, Is.Null);
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
        await StoreAsync(new FundingRatePoint(Btc.Id, Hour.AddHours(2), rate: 5m));

        FundingRatePointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(points.Points, Is.Empty);
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task The_points_of_the_window_come_oldest_first_as_they_are_stored()
    {
        // A point before the window, one at its end and one of another instrument are not of it; the minutes 10:02 to 10:04 were not
        // collected, and nothing stands in for them.
        await StoreAsync(
            new FundingRatePoint(Btc.Id, Hour.AddMinutes(5), rate: 0.00000331m),
            new FundingRatePoint(Btc.Id, Hour.AddMinutes(-1), rate: 1m),
            new FundingRatePoint(Btc.Id, Hour, rate: 2m),
            new FundingRatePoint(Btc.Id, Hour.AddMinutes(1), rate: 3m),
            new FundingRatePoint(Btc.Id, Hour.AddMinutes(10), rate: 4m),
            new FundingRatePoint(Eth.Id, Hour.AddMinutes(1), rate: 5m));

        FundingRatePointsInternal points = await ReadAsync(Btc.Id, Hour, Hour.AddMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(
                points.Points,
                Is.EqualTo(
                    new[]
                    {
                        new FundingRatePointInternal(Hour, 2m),
                        new FundingRatePointInternal(Hour.AddMinutes(1), 3m),
                        new FundingRatePointInternal(Hour.AddMinutes(5), 0.00000331m)
                    }));
            Assert.That(points.ContinueFrom, Is.Null);
        });
    }
}