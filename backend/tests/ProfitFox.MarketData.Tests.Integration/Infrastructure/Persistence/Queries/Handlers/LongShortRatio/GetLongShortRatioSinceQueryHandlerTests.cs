using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

[TestFixture]
public class GetLongShortRatioSinceQueryHandlerTests
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

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    // The ids put BTC before ETH among the points of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILongShortRatioSeriesRepository _series = null!;

    private async Task<IReadOnlyList<InstrumentLongShortRatioInternal>> ReadAsync(DateTimeOffset since)
    {
        var read = new List<InstrumentLongShortRatioInternal>();
        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();

        await foreach (InstrumentLongShortRatioInternal item in sender.CreateStream(new GetLongShortRatioSinceQuery(since)))
        {
            read.Add(item);
        }

        return read;
    }

    // A series of the instrument that covers the hour around noon with the points.
    private Task LoadAsync(Instrument instrument, params (DateTimeOffset Time, decimal Long)[] points)
    {
        return _series.SaveAsync(
            LongShortRatioSeries.Create(
                instrument.Id,
                Noon.AddMinutes(-30),
                Noon.AddMinutes(30),
                [.. points.Select(point => new LongShortRatioPoint(instrument.Id, point.Time, point.Long, 1m - point.Long))]),
            CancellationToken.None);
    }

    [Test]
    public async Task Nothing_stored_since_the_time_gives_nothing()
    {
        await LoadAsync(Btc, (Noon, 0.7m));

        Assert.That(await ReadAsync(Noon.AddMinutes(5)), Is.Empty);
    }

    [Test]
    public async Task The_points_of_every_instrument_at_or_after_the_time_come_oldest_first_exactly_as_stored()
    {
        await LoadAsync(Eth, (Noon.AddMinutes(5), 0.6001m));
        await LoadAsync(Btc, (Noon.AddMinutes(5), 0.726m), (Noon, 0.7302m), (Noon.AddMinutes(-5), 0.1m));

        IReadOnlyList<InstrumentLongShortRatioInternal> read = await ReadAsync(Noon);

        Assert.That(
            read,
            Is.EqualTo(
                new[]
                {
                    new InstrumentLongShortRatioInternal(Btc.Id, Noon, Long: 0.7302m, Short: 0.2698m),
                    new InstrumentLongShortRatioInternal(Btc.Id, Noon.AddMinutes(5), Long: 0.726m, Short: 0.274m),
                    new InstrumentLongShortRatioInternal(Eth.Id, Noon.AddMinutes(5), Long: 0.6001m, Short: 0.3999m)
                }));
    }
}