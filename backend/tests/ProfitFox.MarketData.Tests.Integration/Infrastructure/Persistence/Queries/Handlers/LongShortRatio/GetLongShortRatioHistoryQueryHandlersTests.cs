using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     The coverage and the points of the history of the long/short ratio: the two reads of one call.
/// </summary>
[TestFixture]
public class GetLongShortRatioHistoryQueryHandlersTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Ten = Today.AddDays(-3).AddHours(10);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    [Test]
    public async Task The_points_of_the_window_come_with_the_coverage_of_the_series_cut_to_it()
    {
        await _scope.ServiceProvider.GetRequiredService<ILongShortRatioSeriesRepository>()
            .SaveAsync(
                LongShortRatioSeries.Create(
                    Btc.Id,
                    Ten.AddMinutes(-5),
                    Ten.AddMinutes(55),
                    [
                        new LongShortRatioPoint(Btc.Id, Ten.AddMinutes(-5), longShare: 0.7m, shortShare: 0.3m),
                        new LongShortRatioPoint(Btc.Id, Ten, longShare: 0.726m, shortShare: 0.274m),
                        new LongShortRatioPoint(Btc.Id, Ten.AddMinutes(25), longShare: 0.7302m, shortShare: 0.2698m),
                        new LongShortRatioPoint(Btc.Id, Ten.AddMinutes(50), longShare: 0.7m, shortShare: 0.3m)
                    ]),
                CancellationToken.None);
        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();

        IReadOnlyList<InstrumentCoverageInternal> coverage =
            await sender.Send(new GetLongShortRatioHistoryCoverageQuery([Btc.Id], [], new HistoryWindow(Ten, Ten.AddMinutes(50), Today)));
        var points = new List<InstrumentLongShortRatioInternal>();

        await foreach (InstrumentLongShortRatioInternal point in sender.CreateStream(new GetLongShortRatioHistoryQuery(coverage)))
        {
            points.Add(point);
        }

        Assert.Multiple(() =>
        {
            Assert.That(coverage.Single().Ranges, Is.EqualTo(new[] { new TimeRangeInternal(Ten, Ten.AddMinutes(50)) }));
            Assert.That(
                points,
                Is.EqualTo(
                    new[]
                    {
                        new InstrumentLongShortRatioInternal(Btc.Id, Ten, 0.726m, 0.274m),
                        new InstrumentLongShortRatioInternal(Btc.Id, Ten.AddMinutes(25), 0.7302m, 0.2698m)
                    }));
        });
    }
}